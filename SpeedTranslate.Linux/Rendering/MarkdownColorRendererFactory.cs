using System;
using System.Collections.Generic;
using Avalonia.Media;
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
        ["key"] = Style("#FBBF24"),
        ["term"] = Style("#C084FC"),
        ["accent"] = Style("#38BDF8"),
        ["warn"] = Style("#F87171"),
        ["warning"] = Style("#F87171"),
        ["ok"] = Style("#34D399"),
        ["success"] = Style("#34D399"),
        ["note"] = Style("#60A5FA"),
        ["info"] = Style("#60A5FA"),
        ["emphasis"] = Style("#FBBF24"),
    };

    public string Mode => MarkdownColorRenderModes.SemanticTags;

    public string PromptInstructions =>
        """
        Optional semantic color tags:
        - When it genuinely helps scanning, wrap only a few important words or short phrases with these tags:
          <key>core term/name/number</key>
          <term>technical term</term>
          <warn>risk, warning, error, or caveat</warn>
          <ok>positive result, conclusion, or safe action</ok>
          <note>useful note or context</note>
        - Use at most 6 tagged phrases in one response. Do not wrap full sentences or paragraphs.
        - Do not nest these tags. Do not invent other HTML/XML tags.
        - Keep Markdown and LaTeX math delimiters intact outside these tags.
        """;

    private SemanticTagMarkdownColorRenderer()
    {
    }

    public bool TryGetStyle(string tagName, out MarkdownInlineStyle style) =>
        Styles.TryGetValue(tagName, out style);

    private static MarkdownInlineStyle Style(string color) =>
        new(new SolidColorBrush(Color.Parse(color)), FontWeight.SemiBold, null, null);
}
