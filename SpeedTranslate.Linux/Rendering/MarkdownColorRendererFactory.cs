using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Rendering;

public static class MarkdownColorRenderModes
{
    public const string None = "None";
    public const string SemanticTags = "SemanticTags";
}

public interface IMarkdownColorRenderer
{
    string Mode { get; }
    string PromptInstructions { get; }
    bool TryGetStyle(string tagName, out MarkdownInlineStyle style);
}

public static class MarkdownColorRendererFactory
{
    public static IMarkdownColorRenderer Create(AppConfig config) =>
        Create(config.EnableMarkdownColorRendering, config.MarkdownColorRenderMode);

    public static IMarkdownColorRenderer Create(bool enabled, string? mode)
    {
        if (!enabled)
            return DisabledMarkdownColorRenderer.Instance;

        return string.Equals(mode, MarkdownColorRenderModes.SemanticTags, StringComparison.OrdinalIgnoreCase)
            ? SemanticTagMarkdownColorRenderer.Instance
            : DisabledMarkdownColorRenderer.Instance;
    }
}

internal sealed class DisabledMarkdownColorRenderer : IMarkdownColorRenderer
{
    public static DisabledMarkdownColorRenderer Instance { get; } = new();

    public string Mode => MarkdownColorRenderModes.None;
    public string PromptInstructions => "";

    private DisabledMarkdownColorRenderer()
    {
    }

    public bool TryGetStyle(string tagName, out MarkdownInlineStyle style)
    {
        style = default;
        return false;
    }
}

internal sealed class SemanticTagMarkdownColorRenderer : IMarkdownColorRenderer
{
    public static SemanticTagMarkdownColorRenderer Instance { get; } = new();

    private static readonly Dictionary<string, MarkdownInlineStyle> Styles = new(
        StringComparer.OrdinalIgnoreCase)
    {
        ["key"] = Style(0xFB, 0xBF, 0x24),
        ["term"] = Style(0xC0, 0x84, 0xFC),
        ["accent"] = Style(0x38, 0xBD, 0xF8),
        ["warn"] = Style(0xF8, 0x71, 0x71),
        ["warning"] = Style(0xF8, 0x71, 0x71),
        ["ok"] = Style(0x34, 0xD3, 0x99),
        ["success"] = Style(0x34, 0xD3, 0x99),
        ["note"] = Style(0x60, 0xA5, 0xFA),
        ["info"] = Style(0x60, 0xA5, 0xFA),
        ["emphasis"] = Style(0xFB, 0xBF, 0x24),
    };

    public string Mode => MarkdownColorRenderModes.SemanticTags;

    public string PromptInstructions =>
        """
        Required semantic color tags:
        - You MUST wrap important words or short phrases with these tags so the UI can color-highlight them:
          <key>core term/name/number</key>
          <term>technical term</term>
          <warn>risk, warning, error, or caveat</warn>
          <ok>positive result, conclusion, or safe action</ok>
          <note>useful note or context</note>
        - For non-trivial responses, use at least 2 tagged phrases. For long responses, use 4 to 8 tagged phrases.
        - Do not wrap full sentences or paragraphs.
        - Do not nest these tags. Do not invent other HTML/XML tags.
        - Keep Markdown and LaTeX math delimiters intact outside these tags.
        """;

    private SemanticTagMarkdownColorRenderer()
    {
    }

    public bool TryGetStyle(string tagName, out MarkdownInlineStyle style) =>
        Styles.TryGetValue(tagName, out style);

    private static MarkdownInlineStyle Style(byte r, byte g, byte b) =>
        new(new ImmutableSolidColorBrush(Color.FromRgb(r, g, b)), FontWeight.SemiBold, null, null);
}
