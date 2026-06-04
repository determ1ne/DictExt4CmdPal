using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DictExt;

internal sealed partial class DictionaryListPage : DynamicListPage
{
    private readonly DictionaryLookupService _lookupService;
    private readonly DictionarySettingsManager _settingsManager;
    private readonly object _sync = new();
    private IListItem[] _items = [];

    public DictionaryListPage(DictionaryLookupService lookupService, DictionarySettingsManager settingsManager, string initialSearch = "")
    {
        _lookupService = lookupService;
        _settingsManager = settingsManager;

        Icon = DictionaryIcons.AppIcon;
        Title = "Dictionary";
        Name = "Lookup words";
        PlaceholderText = "Type a word";
        SearchText = initialSearch;

        Requery(initialSearch);
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        Requery(newSearch);
    }

    public override IListItem[] GetItems()
    {
        lock (_sync)
        {
            return _items;
        }
    }

    private void Requery(string query)
    {
        IListItem[] items;
        if (_lookupService.DictionaryCount == 0)
        {
            items =
            [
                new ListItem(_settingsManager.Settings.SettingsPage)
                {
                    Icon = Icon,
                    Title = "Add .mdx dictionaries",
                    Subtitle = $"Put .mdx files in {DictionaryLookupService.DefaultDictionaryDirectory} or add paths in settings.",
                },
            ];
        }
        else
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                items = [];
            }
            else
            {
                IReadOnlyList<DictionarySearchResult> results = _lookupService.SearchHeadwords(query, 25);
                items = results.Count == 0
                    ? [CreateEmptyItem(query)]
                    : results.Select(CreateResultItem).ToArray();
            }
        }

        lock (_sync)
        {
            _items = items;
        }

        RaiseItemsChanged();
    }

    private ListItem CreateResultItem(DictionarySearchResult result)
    {
        return new ListItem(new DictionaryArticlePage(_lookupService, result))
        {
            Icon = Icon,
            Title = result.Headword.Text,
            Subtitle = result.DictionaryTitle,
        };
    }

    private ListItem CreateEmptyItem(string query)
    {
        string message = _lookupService.LastError ?? "No matching headwords.";
        return new ListItem(new NoOpCommand())
        {
            Icon = Icon,
            Title = string.IsNullOrWhiteSpace(query) ? "Start typing to search" : $"No results for {query}",
            Subtitle = message,
            MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
        };
    }
}
