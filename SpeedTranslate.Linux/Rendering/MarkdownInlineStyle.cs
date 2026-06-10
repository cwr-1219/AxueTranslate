using Avalonia.Media;

namespace SpeedTranslate.Linux.Rendering;

public readonly record struct MarkdownInlineStyle(
    IBrush? Foreground,
    FontWeight? FontWeight,
    double? FontSize,
    string? FontFamily)
{
    public MarkdownInlineStyle Merge(MarkdownInlineStyle next) =>
        new(
            next.Foreground ?? Foreground,
            next.FontWeight ?? FontWeight,
            next.FontSize ?? FontSize,
            next.FontFamily ?? FontFamily);
}
