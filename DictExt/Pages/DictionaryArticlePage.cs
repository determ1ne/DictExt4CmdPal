using System.Net;
using System.Text.RegularExpressions;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DictExt;

internal sealed partial class DictionaryArticlePage : ContentPage
{
    private readonly DictionaryLookupService _lookupService;
    private readonly DictionarySearchResult _result;

    public DictionaryArticlePage(DictionaryLookupService lookupService, DictionarySearchResult result)
    {
        _lookupService = lookupService;
        _result = result;

        Icon = DictionaryIcons.AppIcon;
        Name = result.Headword.Text;
        Title = result.DictionaryTitle;
    }

    public override IContent[] GetContent()
    {
        try
        {
            string article = _lookupService.ReadArticle(_result);
            string body = WrapArticleHtml(_result.Headword.Text, article);
            return [new MarkdownContent(body)];
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or MdictSharp.MdictException)
        {
            return [new MarkdownContent(WrapArticleHtml(_result.Headword.Text, WebUtility.HtmlEncode(ex.Message)))];
        }
    }

    private static string WrapArticleHtml(string headword, string article)
    {
        const string fontStack = "\"Microsoft YaHei UI\", \"Microsoft YaHei\", SimSun, \"Noto Sans CJK SC\", \"Source Han Sans SC\", Arial, sans-serif";
        string baseStyle = $"font-family:{fontStack};";
        string styledArticle = InjectFontStyle(article, baseStyle);

        return $$"""
            <article lang="zh-CN" style="{{baseStyle}} line-height:1.55;">
              <h1 style="{{baseStyle}}">{{WebUtility.HtmlEncode(headword)}}</h1>
              {{styledArticle}}
            </article>
            """;
    }

    private static string InjectFontStyle(string html, string style)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return html;
        }

        return HtmlTagRegex().Replace(html, match =>
        {
            string tagName = match.Groups["name"].Value;
            string attributes = match.Groups["attrs"].Value;
            if (tagName.Equals("style", StringComparison.OrdinalIgnoreCase) ||
                tagName.Equals("script", StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            string trailingSlash = string.Empty;
            if (attributes.EndsWith('/'))
            {
                trailingSlash = "/";
                attributes = attributes[..^1].TrimEnd();
            }

            bool hasStyle = StyleAttributeRegex().IsMatch(attributes);
            if (!hasStyle)
            {
                return $"<{tagName}{attributes} style=\"{style}\"{trailingSlash}>";
            }

            string updatedAttributes = StyleAttributeRegex().Replace(
                attributes,
                styleMatch =>
                {
                    string quote = styleMatch.Groups["quote"].Value;
                    string existingStyle = styleMatch.Groups["value"].Value;
                    return $" style={quote}{style} {existingStyle}{quote}";
                },
                1);

            return $"<{tagName}{updatedAttributes}{trailingSlash}>";
        });
    }

    [GeneratedRegex("<(?!/|!)(?<name>[a-zA-Z][\\w:-]*)(?<attrs>[^>]*)>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\sstyle\\s*=\\s*(?<quote>[\"'])(?<value>.*?)\\k<quote>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex StyleAttributeRegex();
}
