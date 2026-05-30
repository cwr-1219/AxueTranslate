using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// 通过 xclip 子进程读写 X11 CLIPBOARD selection。也提供 PRIMARY selection
/// (鼠标自动选中) 的快速读取，这是 Linux 桌面"划词"翻译的最自然路径。
/// </summary>
public sealed class ClipboardService
{
    public Task<string> GetClipboardTextAsync(int retryCount = 5, int delayMs = 80) =>
        GetSelectionWithRetryAsync("clipboard", retryCount, delayMs);

    public Task<string> GetPrimarySelectionAsync(int retryCount = 2, int delayMs = 30) =>
        GetSelectionWithRetryAsync("primary", retryCount, delayMs);

    public async Task<bool> SetClipboardTextAsync(string text, int retryCount = 5, int delayMs = 80)
    {
        for (var i = 0; i < retryCount; i++)
        {
            try
            {
                if (await WriteToXclipAsync(text))
                    return true;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Clipboard] write attempt {i + 1} failed: {ex.Message}");
            }
            if (i < retryCount - 1) await Task.Delay(delayMs);
        }
        return false;
    }

    private static async Task<string> GetSelectionWithRetryAsync(string sel, int retryCount, int delayMs)
    {
        for (var i = 0; i < retryCount; i++)
        {
            try
            {
                var text = await ReadFromXclipAsync(sel);
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Clipboard] read {sel} attempt {i + 1} failed: {ex.Message}");
            }
            if (i < retryCount - 1) await Task.Delay(delayMs);
        }
        return string.Empty;
    }

    private static async Task<string> ReadFromXclipAsync(string selection)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xclip",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-selection");
        psi.ArgumentList.Add(selection);
        psi.ArgumentList.Add("-o");

        using var proc = Process.Start(psi);
        if (proc == null) return string.Empty;
        var output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return output;
    }

    private static async Task<bool> WriteToXclipAsync(string text)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "xclip",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-selection");
        psi.ArgumentList.Add("clipboard");
        psi.ArgumentList.Add("-i");

        using var proc = Process.Start(psi);
        if (proc == null) return false;
        await proc.StandardInput.WriteAsync(text);
        proc.StandardInput.Close();
        await proc.WaitForExitAsync();
        return proc.ExitCode == 0;
    }
}
