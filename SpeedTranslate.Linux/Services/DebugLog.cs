using System;
using System.IO;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// 简单的调试日志，写到 ~/.config/AxueTranslate/debug.log
/// </summary>
public static class DebugLog
{
    private static readonly object Sync = new();
    private static readonly string LogPath = Path.Combine(ConfigManager.ConfigDir, "debug.log");

    public static void Write(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(ConfigManager.ConfigDir);
                File.AppendAllText(LogPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
            }
        }
        catch
        {
        }
    }

    public static void Clear()
    {
        try
        {
            lock (Sync)
            {
                if (File.Exists(LogPath)) File.Delete(LogPath);
            }
        }
        catch
        {
        }
    }
}
