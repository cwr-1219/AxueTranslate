using SpeedTranslate.Linux.Models;

namespace SpeedTranslate.Linux.Rendering;

public readonly record struct MarkdownRenderOptions(
    bool EnableMathRendering,
    bool EnableColorRendering,
    string ColorRenderMode)
{
    public static MarkdownRenderOptions Default { get; } =
        new(true, false, MarkdownColorRenderModes.SemanticTags);

    public static MarkdownRenderOptions FromConfig(AppConfig config) =>
        new(
            config.EnableMarkdownMathRendering,
            config.EnableMarkdownColorRendering,
            config.MarkdownColorRenderMode);
}
