using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MdictSharp;

#pragma warning disable CA1711
public sealed partial class MdxDictionary : IDisposable
#pragma warning restore CA1711
{
    private const int EncryptedHeadwordIndex = 2;

    private readonly FileStream _stream;
    private readonly List<HeadwordEntry> _entries;
    private readonly Dictionary<string, List<HeadwordEntry>> _lookup;
    private readonly List<RecordBlockIndex> _recordBlocks;
    private readonly Dictionary<int, (string Prefix, string Suffix)> _styleSheets;
    private readonly Dictionary<int, byte[]> _recordBlockCache = [];
    private readonly Encoding _articleEncoding;
    private readonly bool _keyCaseSensitive;
    private readonly bool _stripKey;
    private readonly long _recordBlocksStart;

    private MdxDictionary(
        FileStream stream,
        MdxMetadata metadata,
        List<HeadwordEntry> entries,
        List<RecordBlockIndex> recordBlocks,
        Dictionary<int, (string Prefix, string Suffix)> styleSheets,
        Encoding articleEncoding,
        bool keyCaseSensitive,
        bool stripKey,
        long recordBlocksStart)
    {
        _stream = stream;
        Metadata = metadata;
        _entries = entries;
        _recordBlocks = recordBlocks;
        _styleSheets = styleSheets;
        _articleEncoding = articleEncoding;
        _keyCaseSensitive = keyCaseSensitive;
        _stripKey = stripKey;
        _recordBlocksStart = recordBlocksStart;

        _lookup = [];
        foreach (HeadwordEntry entry in entries)
        {
            string key = NormalizeKey(entry.Text);
            if (!_lookup.TryGetValue(key, out List<HeadwordEntry>? list))
            {
                list = [];
                _lookup[key] = list;
            }

            list.Add(entry);
        }
    }

    public MdxMetadata Metadata { get; }

    public static MdxDictionary Open(string path)
    {
        FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Load(stream, path);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public IEnumerable<MdxHeadword> EnumerateHeadwords()
    {
        foreach (HeadwordEntry entry in _entries)
        {
            yield return ToHeadword(entry);
        }
    }

    public IReadOnlyList<MdxHeadword> SearchHeadwords(string query, int maxResults)
    {
        if (maxResults <= 0)
        {
            return [];
        }

        string normalizedQuery = NormalizeKey(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return [];
        }

        List<MdxHeadword> results = [];
        foreach (HeadwordEntry entry in _entries)
        {
            if (NormalizeKey(entry.Text).StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                results.Add(ToHeadword(entry));
                if (results.Count >= maxResults)
                {
                    return results;
                }
            }
        }

        foreach (HeadwordEntry entry in _entries)
        {
            string normalized = NormalizeKey(entry.Text);
            if (!normalized.StartsWith(normalizedQuery, StringComparison.Ordinal) &&
                normalized.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                results.Add(ToHeadword(entry));
                if (results.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return results;
    }

    public IReadOnlyList<string> Lookup(string word)
    {
        return TryLookup(word, out IReadOnlyList<string>? articles) ? articles : [];
    }

    public string ReadArticle(MdxHeadword headword)
    {
        HeadwordEntry entry = new(headword.Text, headword.RecordOffset, headword.RecordSize);
        return ReadArticle(entry);
    }

    public bool TryLookup(string word, out IReadOnlyList<string> articles)
    {
        if (!_lookup.TryGetValue(NormalizeKey(word), out List<HeadwordEntry>? entries))
        {
            articles = [];
            return false;
        }

        var results = new List<string>(entries.Count);
        foreach (HeadwordEntry entry in entries)
        {
            results.Add(ReadArticle(entry));
        }

        articles = results;
        return results.Count > 0;
    }

    private static MdxDictionary Load(FileStream stream, string path)
    {
        using var reader = new BigEndianReader(stream, leaveOpen: true);
        int headerSize = reader.ReadInt32();
        byte[] headerBytes = reader.ReadBytes(headerSize);
        uint headerChecksum = BinaryPrimitives.ReadUInt32LittleEndian(reader.ReadBytes(4));
        uint actualHeaderChecksum = Adler32.Compute(headerBytes);
        if (actualHeaderChecksum != headerChecksum)
        {
            throw new MdictException($"Header checksum mismatch. Expected 0x{headerChecksum:x8}, got 0x{actualHeaderChecksum:x8}.");
        }

        Header header = ParseHeader(headerBytes, path);
        int numberSize = header.GeneratedByEngineVersion < 2.0 ? 4 : 8;

        long keywordHeaderStart = reader.Position;
        long numHeadwordBlocks = reader.ReadNumber(numberSize);
        long wordCount = reader.ReadNumber(numberSize);
        long headwordBlockInfoDecompressedSize = header.GeneratedByEngineVersion >= 2.0
            ? reader.ReadNumber(numberSize)
            : -1;
        long headwordBlockInfoSize = reader.ReadNumber(numberSize);
        long headwordBlockSize = reader.ReadNumber(numberSize);

        if (header.GeneratedByEngineVersion >= 2.0)
        {
            byte[] keywordHeaderBytes = new byte[numberSize * 5];
            stream.Position = keywordHeaderStart;
            stream.ReadExactly(keywordHeaderBytes);
            uint keywordHeaderChecksum = reader.ReadUInt32();
            uint actualKeywordHeaderChecksum = Adler32.Compute(keywordHeaderBytes);
            if (actualKeywordHeaderChecksum != keywordHeaderChecksum)
            {
                throw new MdictException("Headword header checksum mismatch.");
            }
        }

        long headwordBlockInfoStart = reader.Position;
        byte[] headwordBlockInfoBytes = reader.ReadBytes(checked((int)headwordBlockInfoSize));
        if (header.GeneratedByEngineVersion >= 2.0)
        {
            if ((header.Encrypted & EncryptedHeadwordIndex) != 0)
            {
                MdictEncryption.DecryptHeadwordIndex(headwordBlockInfoBytes);
            }

            headwordBlockInfoBytes = MdictCompression.Decompress(headwordBlockInfoBytes, headwordBlockInfoDecompressedSize);
        }

        List<HeadwordBlockInfo> headwordBlocks = DecodeHeadwordBlockInfo(
            headwordBlockInfoBytes,
            header.ArticleEncoding,
            numberSize,
            header.GeneratedByEngineVersion);

        if (headwordBlocks.Count != numHeadwordBlocks)
        {
            throw new MdictException($"Headword block count mismatch. Expected {numHeadwordBlocks}, got {headwordBlocks.Count}.");
        }

        long headwordBlocksStart = reader.Position;
        var entries = new List<HeadwordEntry>(wordCount <= int.MaxValue ? (int)wordCount : 0);
        foreach (HeadwordBlockInfo blockInfo in headwordBlocks)
        {
            byte[] compressed = reader.ReadBytes(checked((int)blockInfo.CompressedSize));
            byte[] decompressed = MdictCompression.Decompress(compressed, blockInfo.DecompressedSize);
            entries.AddRange(SplitHeadwordBlock(decompressed, header.ArticleEncoding, numberSize));
        }

        long recordInfoHeaderStart = headwordBlockInfoStart + headwordBlockInfoSize + headwordBlockSize;
        stream.Position = recordInfoHeaderStart;
        long numRecordBlocks = reader.ReadNumber(numberSize);
        reader.ReadNumber(numberSize);
        long recordBlockInfoSize = reader.ReadNumber(numberSize);
        reader.ReadNumber(numberSize);
        long recordBlockInfoStart = reader.Position;
        long recordBlocksStart = recordBlockInfoStart + recordBlockInfoSize;

        var recordBlocks = new List<RecordBlockIndex>(numRecordBlocks <= int.MaxValue ? (int)numRecordBlocks : 0);
        long compressedOffset = 0;
        long decompressedOffset = 0;
        for (int i = 0; i < numRecordBlocks; i++)
        {
            long compressedSize = reader.ReadNumber(numberSize);
            long decompressedSize = reader.ReadNumber(numberSize);
            recordBlocks.Add(new RecordBlockIndex(i, compressedOffset, decompressedOffset, decompressedOffset + decompressedSize, compressedSize, decompressedSize));
            compressedOffset += compressedSize;
            decompressedOffset += decompressedSize;
        }

        CompleteRecordSizes(entries, recordBlocks);

        var metadata = new MdxMetadata(
            header.Title,
            header.Description,
            header.ArticleEncoding.WebName,
            header.Format,
            wordCount,
            header.IsRightToLeft);

        stream.Position = headwordBlocksStart;
        return new MdxDictionary(stream, metadata, entries, recordBlocks, header.StyleSheets, header.ArticleEncoding, header.KeyCaseSensitive, header.StripKey, recordBlocksStart);
    }

    private static Header ParseHeader(byte[] headerBytes, string path)
    {
        string headerText = Encoding.Unicode.GetString(headerBytes).TrimEnd('\0');
        Dictionary<string, string> attributes = ParseHeaderAttributes(headerText);

        string title = Attribute(attributes, "Title");
        if (string.IsNullOrWhiteSpace(title) || title == "Title (No HTML code allowed)")
        {
            title = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            title = WebUtility.HtmlDecode(StripHtml(title));
        }

        string description = WebUtility.HtmlDecode(StripHtml(Attribute(attributes, "Description")));
        string encodingName = Attribute(attributes, "Encoding");
        Encoding articleEncoding = ResolveEncoding(encodingName);
        double version = double.TryParse(Attribute(attributes, "GeneratedByEngineVersion"), out double parsedVersion)
            ? parsedVersion
            : 1.0;

        return new Header(
            version,
            int.TryParse(Attribute(attributes, "Encrypted"), out int encrypted) ? encrypted : 0,
            title,
            description,
            articleEncoding,
            Attribute(attributes, "Format"),
            Attribute(attributes, "Left2Right") != "Yes",
            Attribute(attributes, "KeyCaseSensitive") == "Yes",
            Attribute(attributes, "StripKey") == "Yes",
            ParseStyleSheets(Attribute(attributes, "StyleSheet")));
    }

    private static Dictionary<string, string> ParseHeaderAttributes(string headerText)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(headerText, @"\s([A-Za-z0-9_]+)=""([^""]*)""", RegexOptions.Singleline))
        {
            attributes[match.Groups[1].Value] = WebUtility.HtmlDecode(match.Groups[2].Value);
        }

        if (attributes.Count == 0)
        {
            throw new MdictException("Unable to parse MDict header attributes.");
        }

        return attributes;
    }

    private static Encoding ResolveEncoding(string encodingName)
    {
        if (string.IsNullOrWhiteSpace(encodingName) || encodingName.Equals("UTF-16", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Unicode;
        }

        if (encodingName.Equals("UTF-16LE", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.Unicode;
        }

        if (encodingName.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
        {
            return Encoding.UTF8;
        }

        if (encodingName.Equals("GBK", StringComparison.OrdinalIgnoreCase) ||
            encodingName.Equals("GB2312", StringComparison.OrdinalIgnoreCase) ||
            encodingName.Equals("GB18030", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("GBK/GB18030 dictionaries require code page support, which is outside the no-dependency first version.");
        }

        return Encoding.GetEncoding(encodingName);
    }

    private static Dictionary<int, (string Prefix, string Suffix)> ParseStyleSheets(string value)
    {
        var result = new Dictionary<int, (string Prefix, string Suffix)>();
        if (string.IsNullOrEmpty(value))
        {
            return result;
        }

        string[] lines = Regex.Split(value, @"\r\n|\n|\r");
        for (int i = 0; i + 2 < lines.Length; i += 3)
        {
            if (int.TryParse(lines[i], out int id))
            {
                result[id] = (WebUtility.HtmlDecode(lines[i + 1]), WebUtility.HtmlDecode(lines[i + 2]));
            }
        }

        return result;
    }

    private static List<HeadwordBlockInfo> DecodeHeadwordBlockInfo(byte[] bytes, Encoding encoding, int numberSize, double version)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BigEndianReader(stream);
        var result = new List<HeadwordBlockInfo>();
        bool isVersion2 = version >= 2.0;

        while (stream.Position < stream.Length)
        {
            reader.ReadNumber(numberSize);
            int firstSize = isVersion2 ? reader.ReadUInt16() : reader.ReadByte();
            SkipText(reader, encoding, firstSize, isVersion2 ? 1 : 0);
            int lastSize = isVersion2 ? reader.ReadUInt16() : reader.ReadByte();
            SkipText(reader, encoding, lastSize, isVersion2 ? 1 : 0);
            long compressedSize = reader.ReadNumber(numberSize);
            long decompressedSize = reader.ReadNumber(numberSize);
            result.Add(new HeadwordBlockInfo(compressedSize, decompressedSize));
        }

        return result;
    }

    private static List<HeadwordEntry> SplitHeadwordBlock(byte[] bytes, Encoding encoding, int numberSize)
    {
        using var stream = new MemoryStream(bytes);
        using var reader = new BigEndianReader(stream);
        var result = new List<HeadwordEntry>();

        while (stream.Position < stream.Length)
        {
            long offset = reader.ReadNumber(numberSize);
            string headword = reader.ReadNullTerminatedString(encoding);
            result.Add(new HeadwordEntry(headword, offset, 0));
        }

        return result;
    }

    private static void CompleteRecordSizes(List<HeadwordEntry> entries, List<RecordBlockIndex> recordBlocks)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            long start = entries[i].RecordOffset;
            long end = i + 1 < entries.Count ? entries[i + 1].RecordOffset : FindRecordBlock(recordBlocks, start).ShadowEnd;
            entries[i] = entries[i] with { RecordSize = end - start };
        }
    }

    private static RecordBlockIndex FindRecordBlock(List<RecordBlockIndex> recordBlocks, long recordOffset)
    {
        int lo = 0;
        int hi = recordBlocks.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            RecordBlockIndex block = recordBlocks[mid];
            if (recordOffset >= block.ShadowStart && recordOffset < block.ShadowEnd)
            {
                return block;
            }

            if (recordOffset >= block.ShadowEnd)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        throw new MdictException($"Unable to locate record block for offset {recordOffset}.");
    }

    private string ReadArticle(HeadwordEntry entry)
    {
        RecordBlockIndex block = FindRecordBlock(_recordBlocks, entry.RecordOffset);
        if (!_recordBlockCache.TryGetValue(block.Index, out byte[]? decompressed))
        {
            _stream.Position = _recordBlocksStart + block.CompressedOffset;
            byte[] compressed = new byte[block.CompressedSize];
            _stream.ReadExactly(compressed);
            decompressed = MdictCompression.Decompress(compressed, block.DecompressedSize);
            _recordBlockCache[block.Index] = decompressed;
        }

        int offset = checked((int)(entry.RecordOffset - block.ShadowStart));
        int size = checked((int)entry.RecordSize);
        string article = _articleEncoding.GetString(decompressed, offset, size).TrimEnd('\0');
        return SubstituteStyleSheet(article);
    }

    private static MdxHeadword ToHeadword(HeadwordEntry entry)
    {
        return new MdxHeadword(entry.Text, entry.RecordOffset, entry.RecordSize);
    }

    private string SubstituteStyleSheet(string article)
    {
        if (_styleSheets.Count == 0 || article.IndexOf('`', StringComparison.Ordinal) < 0)
        {
            return article;
        }

        string endStyle = string.Empty;
        return Regex.Replace(article, @"`(\d+)`", match =>
        {
            int id = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (_styleSheets.TryGetValue(id, out (string Prefix, string Suffix) style))
            {
                string replacement = endStyle + style.Prefix;
                endStyle = style.Suffix;
                return replacement;
            }

            string fallback = endStyle;
            endStyle = string.Empty;
            return fallback;
        });
    }

    private static void SkipText(BigEndianReader reader, Encoding encoding, int textSize, int terminatorSize)
    {
        int bytes = encoding.CodePage == Encoding.Unicode.CodePage
            ? (textSize + terminatorSize) * 2
            : textSize + terminatorSize;
        _ = reader.ReadBytes(bytes);
    }

    private string NormalizeKey(string key)
    {
        string result = _stripKey ? key.Trim() : key;
        return _keyCaseSensitive ? result : result.ToLowerInvariant();
    }

    private static string Attribute(Dictionary<string, string> attributes, string name)
    {
        return attributes.TryGetValue(name, out string? value) ? value : string.Empty;
    }

    private static string StripHtml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline);
    }

    public void Dispose()
    {
        _stream.Dispose();
    }

    private sealed record Header(
        double GeneratedByEngineVersion,
        int Encrypted,
        string Title,
        string Description,
        Encoding ArticleEncoding,
        string Format,
        bool IsRightToLeft,
        bool KeyCaseSensitive,
        bool StripKey,
        Dictionary<int, (string Prefix, string Suffix)> StyleSheets);

    private sealed record HeadwordBlockInfo(long CompressedSize, long DecompressedSize);

    private sealed record HeadwordEntry(string Text, long RecordOffset, long RecordSize);

    private sealed record RecordBlockIndex(
        int Index,
        long CompressedOffset,
        long ShadowStart,
        long ShadowEnd,
        long CompressedSize,
        long DecompressedSize);
}
