using Microsoft.CommandPalette.Extensions.Toolkit;

namespace DictExt;

internal sealed class DictionarySettingsManager : JsonSettingsManager
{
    private const string SettingsNamespace = "dictext";

    private readonly TextSetting _dictionaryPaths = new(
        Namespaced(nameof(DictionaryPaths)),
        "Dictionary paths",
        "One .mdx file or folder path per line.",
        string.Empty)
    {
        Multiline = true,
        Placeholder = $"{DefaultDictionaryDirectory}{Environment.NewLine}{Path.Combine(DefaultDictionaryDirectory, "example.mdx")}",
    };

    public event EventHandler? SettingsChanged;

    public DictionarySettingsManager()
    {
        FilePath = SettingsJsonPath();
        Settings.Add(_dictionaryPaths);

        LoadSettings();
        Settings.SettingsChanged += (_, _) =>
        {
            SaveSettings();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    public string DictionaryPaths => _dictionaryPaths.Value ?? string.Empty;

    public static string DefaultDictionaryDirectory =>
        Path.Combine(Utilities.BaseSettingsPath("Microsoft.CmdPal"), "Dictionaries");

    public IEnumerable<string> GetConfiguredDictionaryPaths()
    {
        foreach (string line in DictionaryPaths.Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            string path = line.Trim().Trim('"');
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return path;
            }
        }
    }

    private static string Namespaced(string propertyName) => $"{SettingsNamespace}.{propertyName}";

    private static string SettingsJsonPath()
    {
        string directory = Utilities.BaseSettingsPath("Microsoft.CmdPal");
        Directory.CreateDirectory(directory);
        Directory.CreateDirectory(DefaultDictionaryDirectory);
        return Path.Combine(directory, $"{SettingsNamespace}.settings.json");
    }
}
