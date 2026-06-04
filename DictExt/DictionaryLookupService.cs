using MdictSharp;

namespace DictExt;

internal sealed partial class DictionaryLookupService : IDisposable
{
    private readonly DictionarySettingsManager _settings;
    private readonly object _sync = new();
    private readonly List<LoadedDictionary> _dictionaries = [];
    private string[] _loadedPaths = [];
    private string? _lastError;

    public DictionaryLookupService(DictionarySettingsManager settings)
    {
        _settings = settings;
        _settings.SettingsChanged += (_, _) => Reload();
    }

    public static string DefaultDictionaryDirectory => DictionarySettingsManager.DefaultDictionaryDirectory;

    public string? LastError
    {
        get
        {
            lock (_sync)
            {
                EnsureLoaded();
                return _lastError;
            }
        }
    }

    public int DictionaryCount
    {
        get
        {
            lock (_sync)
            {
                EnsureLoaded();
                return _dictionaries.Count;
            }
        }
    }

    public IReadOnlyList<DictionarySearchResult> SearchHeadwords(string query, int maxResults)
    {
        lock (_sync)
        {
            EnsureLoaded();
            if (_dictionaries.Count == 0)
            {
                return [];
            }

            List<DictionarySearchResult> results = [];
            foreach (LoadedDictionary dictionary in _dictionaries)
            {
                foreach (MdxHeadword headword in dictionary.Dictionary.SearchHeadwords(query, maxResults))
                {
                    results.Add(new DictionarySearchResult(dictionary.Path, dictionary.Dictionary.Metadata.Title, headword));
                    if (results.Count >= maxResults)
                    {
                        return results;
                    }
                }
            }

            return results;
        }
    }

    public string ReadArticle(DictionarySearchResult result)
    {
        lock (_sync)
        {
            EnsureLoaded();
            LoadedDictionary? dictionary = _dictionaries.FirstOrDefault(item => string.Equals(item.Path, result.DictionaryPath, StringComparison.OrdinalIgnoreCase));
            if (dictionary is null)
            {
                throw new InvalidOperationException("The dictionary is no longer loaded.");
            }

            return dictionary.Dictionary.ReadArticle(result.Headword);
        }
    }

    public void Reload()
    {
        lock (_sync)
        {
            ClearLoaded();
            EnsureLoaded();
        }
    }

    private void EnsureLoaded()
    {
        string[] paths = GetDictionaryPaths();
        if (_loadedPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ClearLoaded();
        _loadedPaths = paths;

        foreach (string path in paths)
        {
            try
            {
                _dictionaries.Add(new LoadedDictionary(path, MdxDictionary.Open(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or MdictException or NotSupportedException or InvalidDataException)
            {
                _lastError = $"Failed to load {Path.GetFileName(path)}: {ex.Message}";
            }
        }
    }

    private string[] GetDictionaryPaths()
    {
        IEnumerable<string> configured = _settings.GetConfiguredDictionaryPaths()
            .SelectMany(ExpandDictionaryPath);
        IEnumerable<string> defaultDirectoryFiles = Directory.Exists(DefaultDictionaryDirectory)
            ? Directory.EnumerateFiles(DefaultDictionaryDirectory, "*.mdx", SearchOption.TopDirectoryOnly)
            : [];

        return configured
            .Concat(defaultDirectoryFiles)
            .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> ExpandDictionaryPath(string path)
    {
        string expandedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        if (Directory.Exists(expandedPath))
        {
            return Directory.EnumerateFiles(expandedPath, "*.mdx", SearchOption.TopDirectoryOnly);
        }

        return [expandedPath];
    }

    private void ClearLoaded()
    {
        foreach (LoadedDictionary dictionary in _dictionaries)
        {
            dictionary.Dictionary.Dispose();
        }

        _dictionaries.Clear();
        _loadedPaths = [];
        _lastError = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            ClearLoaded();
        }
    }

    private sealed record LoadedDictionary(string Path, MdxDictionary Dictionary);
}

internal sealed record DictionarySearchResult(string DictionaryPath, string DictionaryTitle, MdxHeadword Headword);
