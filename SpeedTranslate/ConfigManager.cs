using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace SpeedTranslate
{
    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (config != null)
                    {
                        // 兼容老配置：如果老配置中没有设置过弹窗快捷键，则回填默认值
                        if (config.TooltipHotkeyKey == Key.None && string.IsNullOrEmpty(config.TooltipHotkeyText))
                        {
                            config.TooltipHotkeyModifiers = ModifierKeys.Control | ModifierKeys.Alt;
                            config.TooltipHotkeyKey = Key.F;
                            config.TooltipHotkeyText = "Ctrl + Alt + F";
                        }
                    }
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
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save config: {ex.Message}");
            }
        }
    }
}
