using System.Text.Json;

namespace XiaomiBE6500Manager.Core;

/// <summary>Persisted user settings (router IP, language) in the per-user app-data folder.</summary>
public sealed class Settings
{
    public string Ip { get; set; } = "192.168.31.1";
    public string Lang { get; set; } = "en";

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XiaomiBE6500Manager");

    static string FilePath => Path.Combine(Directory, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* settings are a convenience; never block the UI */ }
    }
}
