using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// HotkeyModifiers 在 JSON 中序列化为字符串数组（["Control", "Alt"]）。
/// 兼容读取整数和单个字符串两种历史格式。
/// </summary>
public sealed class HotkeyModifiersJsonConverter : JsonConverter<HotkeyModifiers>
{
    public override HotkeyModifiers Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = HotkeyModifiers.None;
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return (HotkeyModifiers)reader.GetInt32();

            case JsonTokenType.String:
                {
                    var s = reader.GetString() ?? "";
                    foreach (var part in s.Split(new[] { ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        result |= ParseOne(part.Trim());
                    }
                    return result;
                }

            case JsonTokenType.StartArray:
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray) break;
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            result |= ParseOne(reader.GetString() ?? "");
                        }
                    }
                    return result;
                }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, HotkeyModifiers value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        if ((value & HotkeyModifiers.Control) != 0) writer.WriteStringValue("Control");
        if ((value & HotkeyModifiers.Alt) != 0) writer.WriteStringValue("Alt");
        if ((value & HotkeyModifiers.Shift) != 0) writer.WriteStringValue("Shift");
        if ((value & HotkeyModifiers.Meta) != 0) writer.WriteStringValue("Meta");
        writer.WriteEndArray();
    }

    private static HotkeyModifiers ParseOne(string s) => s switch
    {
        "Control" or "Ctrl" => HotkeyModifiers.Control,
        "Alt" => HotkeyModifiers.Alt,
        "Shift" => HotkeyModifiers.Shift,
        "Meta" or "Super" or "Win" or "Windows" => HotkeyModifiers.Meta,
        _ => HotkeyModifiers.None,
    };
}
