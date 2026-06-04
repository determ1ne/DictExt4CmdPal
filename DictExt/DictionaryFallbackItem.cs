using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DictExt;

internal sealed partial class DictionaryFallbackItem : FallbackCommandItem
{
    private const string LookupPrefix = "lookup ";
    private const string FallbackId = "com.azuk.cmdpal.dictext.lookup.fallback";

    private readonly DictionaryLookupService _lookupService;
    private readonly DictionarySettingsManager _settingsManager;

    public DictionaryFallbackItem(DictionaryLookupService lookupService, DictionarySettingsManager settingsManager)
        : base(new NoOpCommand(), "Dictionary", FallbackId)
    {
        _lookupService = lookupService;
        _settingsManager = settingsManager;
        Icon = DictionaryIcons.AppIcon;
        Title = string.Empty;
        Subtitle = string.Empty;
    }

    public override void UpdateQuery(string query)
    {
        Command = new NoOpCommand();
        Title = string.Empty;
        Subtitle = string.Empty;
        MoreCommands = [];

        if (!query.StartsWith(LookupPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string word = query[LookupPrefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(word))
        {
            return;
        }

        if (_lookupService.DictionaryCount == 0)
        {
            Title = "Add .mdx dictionaries";
            Subtitle = $"Put .mdx files in {DictionaryLookupService.DefaultDictionaryDirectory} or add paths in settings.";
            Command = _settingsManager.Settings.SettingsPage;
            return;
        }

        IReadOnlyList<DictionarySearchResult> results = _lookupService.SearchHeadwords(word, 1);
        if (results.Count == 0)
        {
            return;
        }

        DictionarySearchResult first = results[0];
        Title = $"Lookup {first.Headword.Text}";
        Subtitle = first.DictionaryTitle;
        Command = new DictionaryArticlePage(_lookupService, first);

        DictionaryListPage lookupPage = new(_lookupService, _settingsManager, word);
        MoreCommands =
        [
            new CommandContextItem(lookupPage)
            {
                Title = "Open dictionary search",
            },
        ];
    }
}
