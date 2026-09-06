using System.IO;
using System.Text.Json;
using ValheimControl.Models;

namespace ValheimControl.Services;

public class ConfigService
{
    // Same location the v1 PowerShell installer writes to, so v2 can reuse
    // an existing installation without re-running any setup.
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ValheimControl",
        "config.json");

    public static bool ConfigExists => File.Exists(ConfigPath);

    public static string ConfigFilePath => ConfigPath;

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigPath))
        {
            throw new FileNotFoundException(
                $"No configuration found at {ConfigPath}. Run the installer (v1 or v2) first, " +
                "or create this file manually using config.example.json as a template.");
        }

        var json = File.ReadAllText(ConfigPath);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var config = JsonSerializer.Deserialize<AppConfig>(json, options);

        if (config is null)
        {
            throw new InvalidOperationException("config.json could not be parsed.");
        }

        return config;
    }

    public static void Save(AppConfig config)
    {
        var dir = Path.GetDirectoryName(ConfigPath)!;
        Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(ConfigPath, json);
    }
}
