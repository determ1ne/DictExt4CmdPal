// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DictExt;

public partial class DictExtCommandsProvider : CommandProvider
{
    private readonly DictionarySettingsManager _settingsManager = new();
    private readonly DictionaryLookupService _lookupService;
    private readonly ICommandItem[] _commands;
    private readonly IFallbackCommandItem[] _fallbackCommands;

    public DictExtCommandsProvider()
    {
        Id = "com.azuk.cmdpal.dictext";
        DisplayName = "Dictionary";
        Icon = DictionaryIcons.AppIcon;
        Settings = _settingsManager.Settings;
        _lookupService = new DictionaryLookupService(_settingsManager);
        _commands = [
            new CommandItem(new DictionaryListPage(_lookupService, _settingsManager))
            {
                Title = "Dictionary",
                Subtitle = "Lookup words",
                MoreCommands = [new CommandContextItem(Settings.SettingsPage)],
            },
        ];
        _fallbackCommands = [new DictionaryFallbackItem(_lookupService, _settingsManager)];
    }

    public override ICommandItem[] TopLevelCommands()
    {
        return _commands;
    }

    public override IFallbackCommandItem[]? FallbackCommands() => _fallbackCommands;

    public override void Dispose()
    {
        _lookupService.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
