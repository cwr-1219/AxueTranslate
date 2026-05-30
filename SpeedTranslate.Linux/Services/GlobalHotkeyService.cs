using System;
using System.Collections.Generic;
using SharpHook;
using SharpHook.Native;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private TaskPoolGlobalHook? _hook;
    private HotkeyDescriptor? _registered;
    private Action? _callback;
    private HotkeyDescriptor? _registered2;
    private Action? _callback2;

    private bool _ctrl, _alt, _shift, _meta;
    private bool _suppressed;

    public bool IsRunning { get; private set; }

    public void Register(HotkeyDescriptor hotkey, Action callback)
    {
        _registered = hotkey;
        _callback = callback;
    }

    public void Register2(HotkeyDescriptor hotkey, Action callback)
    {
        _registered2 = hotkey;
        _callback2 = callback;
    }

    public void Start()
    {
        if (IsRunning) return;
        _hook = new TaskPoolGlobalHook();
        _hook.KeyPressed += OnKeyPressed;
        _hook.KeyReleased += OnKeyReleased;
        _hook.HookEnabled += (_, _) => DebugLog.Write("[Hook] enabled");
        _hook.HookDisabled += (_, _) => DebugLog.Write("[Hook] disabled");
        IsRunning = true;
        // 在后台运行钩子，捕获任何启动失败
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                DebugLog.Write("[Hook] RunAsync starting");
                await _hook.RunAsync();
                DebugLog.Write("[Hook] RunAsync exited");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Hook] RunAsync exception: {ex.GetType().Name}: {ex.Message}");
                ConfigManager.WriteErrorLog("GlobalHotkeyService.RunAsync", ex);
            }
        });
    }

    public void Stop()
    {
        if (!IsRunning) return;
        try
        {
            _hook?.Dispose();
        }
        catch { }
        _hook = null;
        IsRunning = false;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var k = e.Data.KeyCode;
        switch (k)
        {
            case KeyCode.VcLeftControl:
            case KeyCode.VcRightControl:
                _ctrl = true; return;
            case KeyCode.VcLeftAlt:
            case KeyCode.VcRightAlt:
                _alt = true; return;
            case KeyCode.VcLeftShift:
            case KeyCode.VcRightShift:
                _shift = true; return;
            case KeyCode.VcLeftMeta:
            case KeyCode.VcRightMeta:
                _meta = true; return;
        }

        if (_registered == null || _callback == null) return;

        var pressedMods = HotkeyModifiers.None;
        if (_ctrl) pressedMods |= HotkeyModifiers.Control;
        if (_alt) pressedMods |= HotkeyModifiers.Alt;
        if (_shift) pressedMods |= HotkeyModifiers.Shift;
        if (_meta) pressedMods |= HotkeyModifiers.Meta;

        var avKey = MapKeyCodeToAvaloniaKeyName(k);
        if (avKey == null) return;

        if (_suppressed) return;

        // 主热键
        if (pressedMods == _registered.Modifiers &&
            string.Equals(avKey, _registered.Key, StringComparison.OrdinalIgnoreCase))
        {
            _suppressed = true;
            try
            {
                DebugLog.Write($"[Hook] hotkey matched: {_registered.DisplayText}");
                _callback.Invoke();
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Hook] callback exception: {ex.Message}");
            }
            return;
        }

        // 浮窗热键
        if (_registered2 != null && _callback2 != null &&
            pressedMods == _registered2.Modifiers &&
            string.Equals(avKey, _registered2.Key, StringComparison.OrdinalIgnoreCase))
        {
            _suppressed = true;
            try
            {
                DebugLog.Write($"[Hook] tooltip hotkey matched: {_registered2.DisplayText}");
                _callback2.Invoke();
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Hook] tooltip callback exception: {ex.Message}");
            }
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        var k = e.Data.KeyCode;
        switch (k)
        {
            case KeyCode.VcLeftControl:
            case KeyCode.VcRightControl:
                _ctrl = false; break;
            case KeyCode.VcLeftAlt:
            case KeyCode.VcRightAlt:
                _alt = false; break;
            case KeyCode.VcLeftShift:
            case KeyCode.VcRightShift:
                _shift = false; break;
            case KeyCode.VcLeftMeta:
            case KeyCode.VcRightMeta:
                _meta = false; break;
        }
        // 任何键释放都解锁 _suppressed
        _suppressed = false;
    }

    public static string? MapKeyCodeToAvaloniaKeyName(KeyCode kc)
    {
        // SharpHook KeyCode 命名: VcA..VcZ, Vc0..Vc9, VcF1..VcF24
        var name = kc.ToString();
        if (!name.StartsWith("Vc")) return null;
        name = name[2..];

        // VcA..VcZ -> "A".."Z"
        if (name.Length == 1 && name[0] >= 'A' && name[0] <= 'Z') return name;
        // Vc0..Vc9 -> "D0".."D9"  (Avalonia Key 命名)
        if (name.Length == 1 && name[0] >= '0' && name[0] <= '9') return "D" + name;
        // VcF1..VcF24 -> "F1".."F24"
        if (name.Length >= 2 && name[0] == 'F' && int.TryParse(name.AsSpan(1), out _)) return name;
        // 其他常见键
        return name switch
        {
            "Space" => "Space",
            "Enter" => "Enter",
            "Escape" => "Escape",
            "Tab" => "Tab",
            "Backspace" => "Back",
            "Delete" => "Delete",
            "Home" => "Home",
            "End" => "End",
            "PageUp" => "PageUp",
            "PageDown" => "PageDown",
            "Up" => "Up",
            "Down" => "Down",
            "Left" => "Left",
            "Right" => "Right",
            "Insert" => "Insert",
            _ => name,
        };
    }

    public void Dispose() => Stop();
}
