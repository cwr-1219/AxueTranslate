using System;
using System.IO;
using System.Text.Json;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Services;

public static class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new HotkeyModifiersJsonConverter() },
    };

    public static string ConfigDir
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var baseDir = !string.IsNullOrEmpty(xdg)
                ? xdg
                : Path.Combine(home, ".config");
            return Path.Combine(baseDir, "AxueTranslate");
        }
    }

    public static string ConfigPath => Path.Combine(ConfigDir, "config.json");

    public static string ErrorLogPath => Path.Combine(ConfigDir, "error.log");

    public static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                return config ?? new AppConfig();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
        }

        return new AppConfig();
    }

    public static void SaveConfig(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
        }
    }

    public static void WriteErrorLog(string context, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\n" +
                       $"异常消息: {ex.Message}\n" +
                       $"堆栈: {ex.StackTrace}\n" +
                       new string('-', 60) + "\n";
            File.AppendAllText(ErrorLogPath, line);
        }
        catch
        {
        }
    }
}
