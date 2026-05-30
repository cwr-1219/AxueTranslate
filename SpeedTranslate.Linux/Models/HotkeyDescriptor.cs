using System;
using System.Text;

namespace SpeedTranslate.Linux.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Meta = 8,
}

public sealed class HotkeyDescriptor
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Control | HotkeyModifiers.Alt;

    public string Key { get; set; } = "T";

    public string DisplayText
    {
        get
        {
            var sb = new StringBuilder();
            if ((Modifiers & HotkeyModifiers.Control) != 0) sb.Append("Ctrl + ");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) sb.Append("Alt + ");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) sb.Append("Shift + ");
            if ((Modifiers & HotkeyModifiers.Meta) != 0) sb.Append("Super + ");
            sb.Append(Key);
            return sb.ToString();
        }
    }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Key) &&
        (Modifiers != HotkeyModifiers.None || IsFunctionKey(Key));

    private static bool IsFunctionKey(string key) =>
        key.Length >= 2 && key[0] == 'F' &&
        int.TryParse(key.AsSpan(1), out var n) && n >= 1 && n <= 24;
}
