using System.IO;
using System.Text.Json;

namespace DrawAudit.App;

// User choices remembered between sessions, stored in %APPDATA%\DrawAudit\settings.json.
public sealed class AppSettings
{
    public string? LastFolder { get; set; }
    public string? OutputPath { get; set; }

    // Per documents folder: the Subs the user deselected. Storing deselections (rather than
    // selections) keeps newly added Subs selected by default.
    public Dictionary<string, List<string>> DeselectedSubs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DrawAudit", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s is not null)
                {
                    s.DeselectedSubs = new(s.DeselectedSubs ?? new(), StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt settings file must not stop the app; start with defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Settings are a convenience; failing to save them is not an error for the user.
        }
    }
}
