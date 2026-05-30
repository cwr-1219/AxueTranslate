using System.Diagnostics;
using System.Threading.Tasks;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// 通过 xdotool 子进程模拟键盘事件。比 SharpHook EventSimulator 在 X11
/// 应用程序上更可靠（很多 GTK/Qt 应用对 XTest 直接发送的按键事件响应不稳定）。
/// 每次按键前先把焦点切回 targetWindow，避免悬浮状态窗抢焦点。
/// </summary>
public sealed class InputSimulator
{
    public async Task SendCopyAsync(string? targetWindowId)
    {
        await Task.Delay(120);
        await ActivateAsync(targetWindowId);
        await RunXdotoolAsync("key", "--clearmodifiers", "ctrl+c");
        await Task.Delay(120);
    }

    public async Task SendSelectAllAsync(string? targetWindowId)
    {
        await Task.Delay(80);
        await ActivateAsync(targetWindowId);
        await RunXdotoolAsync("key", "--clearmodifiers", "ctrl+a");
        await Task.Delay(80);
    }

    public async Task SendPasteAsync(string? targetWindowId)
    {
        await Task.Delay(100);
        await ActivateAsync(targetWindowId);
        await RunXdotoolAsync("key", "--clearmodifiers", "ctrl+v");
        await Task.Delay(100);
    }

    private static async Task ActivateAsync(string? windowId)
    {
        if (string.IsNullOrEmpty(windowId)) return;
        await RunXdotoolAsync("windowactivate", "--sync", windowId);
        await Task.Delay(30);
    }

    public static async Task<string?> GetActiveWindowIdAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdotool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("getactivewindow");
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            var output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            return string.IsNullOrEmpty(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    private static async Task RunXdotoolAsync(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdotool",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null) return;
            await proc.WaitForExitAsync();
        }
        catch (System.Exception ex)
        {
            DebugLog.Write($"[InputSimulator] xdotool error: {ex.Message}");
        }
    }
}
