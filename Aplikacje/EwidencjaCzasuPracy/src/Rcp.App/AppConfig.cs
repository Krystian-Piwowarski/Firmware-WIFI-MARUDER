using System.Text.Json;

namespace Rcp.App;

/// <summary>Konfiguracja lokalna stanowiska (ścieżka do bazy). Reszta ustawień jest w bazie.</summary>
public sealed class AppConfig
{
    public string DatabasePath { get; set; } = "";

    public static string DataDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "EwidencjaRCP");

    private static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static AppConfig Load(string[] args)
    {
        var cfg = new AppConfig();
        try
        {
            if (File.Exists(ConfigFile))
                cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigFile)) ?? cfg;
        }
        catch
        {
            // uszkodzony plik konfiguracji – użyj domyślnych
        }

        var dbArg = Array.IndexOf(args, "--db");
        if (dbArg >= 0 && dbArg + 1 < args.Length) cfg.DatabasePath = args[dbArg + 1];
        if (string.IsNullOrWhiteSpace(cfg.DatabasePath)) cfg.DatabasePath = Path.Combine(DataDirectory, "rcp.db");
        return cfg;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
