using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Rendering;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.Views;

public readonly record struct TooltipLayoutMetrics(double Width, double ContentMaxHeight);

public partial class TranslationTooltipWindow : Window
{
    private const double CompactWidth = 420;
    private const double CompactContentMaxHeight = 220;
    private const double MaxLongTextWidth = 680;
    private const double MaxLongTextContentHeight = 560;
    private const string MathFontFamily = "Noto Sans Math, Noto Sans Mono, Consolas, monospace";
    private const int MaxAutoHighlights = 8;
    private static readonly IBrush MathBrush = new ImmutableSolidColorBrush(Color.FromRgb(0x67, 0xE8, 0xF9));
    private static readonly Regex AutoWarnRegex = new(
        "(失败|错误|风险|限制|问题|缺点|不足|挑战|超时|异常)",
        RegexOptions.Compiled);
    private static readonly Regex AutoOkRegex = new(
        "(结论|优势|成功|提升|有效|关键|核心)",
        RegexOptions.Compiled);
    private static readonly Regex AutoAcronymRegex = new(
        @"\b[A-Z][A-Z0-9]{1,}(?:[-_/][A-Z0-9]+)*\b",
        RegexOptions.Compiled);
    private static readonly Regex AutoTechnicalTermRegex = new(
        @"[\u4e00-\u9fffA-Za-z0-9]{1,18}(模型|网络|算法|机制|方法|嵌入|编码|注意力|复杂度|表示|训练|数据|公式|结果)",
        RegexOptions.Compiled);
    private static readonly Regex AutoNumberRegex = new(
        @"\b\d+(?:\.\d+)?(?:%|[a-zA-Z]+)\b",
        RegexOptions.Compiled);

    private bool _isClosing;
    private string _translatedText = "";
    private string _originalText = "";
    private AppConfig? _config;
    private LLMService? _llmService;
    private InputSimulator? _inputSimulator;
    private CancellationToken _shutdownToken;
    private bool _isSummaryMode;

    // 锚点：首次弹出时记录，防止内容变化时浮窗乱跑
    private PixelPoint _anchor = new(-9999, -9999);

    public TranslationTooltipWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RepositionIfNeeded();
    }

    // ── 公开 API ──────────────────────────────────────────────────────────────

    public void ShowTooltip(
        string originalText,
        string translatedText,
        AppConfig config,
        LLMService llmService,
        InputSimulator inputSimulator,
        PixelPoint cursorPos,
        CancellationToken shutdownToken = default,
        bool isSummaryMode = false)
    {
        _isClosing = false;
        _originalText = originalText;
        _translatedText = translatedText;
        _config = config;
        _llmService = llmService;
        _inputSimulator = inputSimulator;
        _shutdownToken = shutdownToken;
        _isSummaryMode = isSummaryMode;

        // 填充内容
        UpdateModeTag();

        var translated = this.FindControl<TextBlock>("TranslatedTextBlock");
        if (translated != null) RenderMarkdown(translated, translatedText, CurrentRenderOptions);

        var original = this.FindControl<TextBlock>("OriginalTextBlock");
        if (original != null)
        {
            if (!string.IsNullOrWhiteSpace(originalText) && originalText.Length < 120)
            {
                original.Text = originalText;
                original.IsVisible = true;
            }
            else
            {
                original.IsVisible = false;
            }
        }

        // 同步语种下拉框
        SyncLanguageComboBox(config.TargetLanguage);

        // 计算锚点（鼠标右下方偏移）
        _anchor = ComputePosition(cursorPos);
        ApplyLayoutForText(translatedText);

        // 先移出屏幕外再 Show，避免闪烁
        Position = new PixelPoint(-9999, -9999);
        Opacity = 0;
        Show();
        Activate();

        // 淡入 + 定位（等 Measure 完成后再精确定位）
        DispatcherTimer.RunOnce(() =>
        {
            RepositionIfNeeded();
            FadeIn();
        }, TimeSpan.FromMilliseconds(30));
    }

    public void UpdateTranslatedText(string text)
    {
        _translatedText = text;
        ApplyLayoutForText(text);
        var tb = this.FindControl<TextBlock>("TranslatedTextBlock");
        if (tb != null) RenderMarkdown(tb, text, CurrentRenderOptions);
        RepositionIfNeeded();
    }

    private MarkdownRenderOptions CurrentRenderOptions =>
        _config == null ? MarkdownRenderOptions.Default : MarkdownRenderOptions.FromConfig(_config);

    private void UpdateModeTag()
    {
        var modelTag = this.FindControl<TextBlock>("ModelTagText");
        if (modelTag != null && _config != null)
            modelTag.Text = $"{(_isSummaryMode ? "摘要" : "划词翻译")} ({_config.SelectedModel})";
    }

    // ── 定位 ──────────────────────────────────────────────────────────────────

    private static PixelPoint ComputePosition(PixelPoint cursor)
    {
        // 偏右下 12/18 px；边缘防护在 RepositionIfNeeded 里做
        return new PixelPoint(cursor.X + 12, cursor.Y + 18);
    }

    private void RepositionIfNeeded()
    {
        if (_anchor.X == -9999) return;

        var screens = Screens;
        if (screens == null) return;

        var screen = screens.ScreenFromPoint(_anchor) ?? screens.Primary;
        if (screen == null) return;

        var workArea = screen.WorkingArea;
        var w = (int)(Bounds.Width == 0 ? 370 : Bounds.Width + 20);
        var h = (int)(Bounds.Height == 0 ? 120 : Bounds.Height + 20);

        var x = _anchor.X;
        var y = _anchor.Y;

        if (x + w > workArea.X + workArea.Width)  x = _anchor.X - w - 10;
        if (y + h > workArea.Y + workArea.Height) y = _anchor.Y - h - 10;
        if (x < workArea.X) x = workArea.X + 10;
        if (y < workArea.Y) y = workArea.Y + 10;

        Position = new PixelPoint(x, y);
    }

    public static TooltipLayoutMetrics CalculateLayoutMetrics(
        int textLength,
        double workAreaWidth,
        double workAreaHeight)
    {
        var width = textLength switch
        {
            <= 300 => CompactWidth,
            <= 900 => 440,
            <= 1800 => 560,
            _ => MaxLongTextWidth,
        };

        var contentMaxHeight = textLength switch
        {
            <= 300 => CompactContentMaxHeight,
            <= 900 => 320,
            <= 1800 => 440,
            _ => MaxLongTextContentHeight,
        };

        var boundedWidth = Math.Min(width, Math.Max(CompactWidth, workAreaWidth - 80));
        var boundedHeight = Math.Min(contentMaxHeight, Math.Max(CompactContentMaxHeight, workAreaHeight - 220));
        return new TooltipLayoutMetrics(boundedWidth, boundedHeight);
    }

    private void ApplyLayoutForText(string text)
    {
        var workArea = Screens?.ScreenFromPoint(_anchor)?.WorkingArea
            ?? Screens?.Primary?.WorkingArea
            ?? new PixelRect(0, 0, 1366, 768);
        var layout = CalculateLayoutMetrics(text.Length, workArea.Width, workArea.Height);

        Width = layout.Width;
        if (this.FindControl<ScrollViewer>("ContentScrollViewer") is { } scrollViewer)
        {
            scrollViewer.MaxHeight = layout.ContentMaxHeight;
        }
    }

    // ── 动画 ──────────────────────────────────────────────────────────────────

    private void FadeIn()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            for (var op = 0.0; op <= 1.0; op += 0.15)
            {
                Opacity = Math.Min(op, 1.0);
                await Task.Delay(12);
            }
            Opacity = 1;
        });
    }

    private async void FadeOutAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        for (var op = 1.0; op > 0; op -= 0.15)
        {
            Opacity = Math.Max(op, 0);
            await Task.Delay(12);
        }
        Hide();
        Opacity = 0;
        _isClosing = false;
    }

    // ── 语种下拉框 ────────────────────────────────────────────────────────────

    private static readonly string[] LangTags =
        { "Auto", "English", "Chinese", "Japanese", "Korean", "French", "German", "Spanish" };

    private void SyncLanguageComboBox(string targetLang)
    {
        var cb = this.FindControl<ComboBox>("LanguageComboBox");
        if (cb == null) return;
        cb.SelectionChanged -= LanguageComboBox_SelectionChanged;
        var idx = Array.IndexOf(LangTags, targetLang);
        cb.SelectedIndex = idx < 0 ? 0 : idx;
        cb.SelectionChanged += LanguageComboBox_SelectionChanged;
    }

    private async void LanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_config == null || _llmService == null || string.IsNullOrWhiteSpace(_originalText)) return;

        var cb = sender as ComboBox;
        if (cb == null) return;

        var idx = cb.SelectedIndex;
        if (idx < 0 || idx >= LangTags.Length) return;
        var newLang = LangTags[idx];
        if (_config.TargetLanguage == newLang) return;

        _config.TargetLanguage = newLang;
        ConfigManager.SaveConfig(_config);

        var tb = this.FindControl<TextBlock>("TranslatedTextBlock");
        if (tb != null) RenderMarkdown(tb, _isSummaryMode ? "正在生成摘要..." : "翻译中...", CurrentRenderOptions);

        try
        {
            _shutdownToken.ThrowIfCancellationRequested();
            var result = _isSummaryMode
                ? await _llmService.SummarizeAsync(_originalText, _config, _shutdownToken)
                : await _llmService.TranslateAsync(_originalText, _config, _shutdownToken);
            _shutdownToken.ThrowIfCancellationRequested();
            UpdateTranslatedText(result);
            SaveHistory(result, _isSummaryMode ? "TooltipSummary" : "TooltipTranslation");
            UpdateModeTag();
        }
        catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            UpdateTranslatedText($"翻译失败: {ex.Message}");
        }
    }

    // ── 按钮事件 ──────────────────────────────────────────────────────────────

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => FadeOutAndClose();

    private async void SummaryButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_config == null || _llmService == null || string.IsNullOrWhiteSpace(_originalText))
            return;

        var btn = this.FindControl<Button>("SummaryBtn");
        if (btn != null)
            btn.IsEnabled = false;

        _isSummaryMode = true;
        UpdateModeTag();
        if (this.FindControl<TextBlock>("TranslatedTextBlock") is { } tb)
            RenderMarkdown(tb, "正在生成摘要...", CurrentRenderOptions);

        try
        {
            _shutdownToken.ThrowIfCancellationRequested();
            var result = await _llmService.SummarizeAsync(_originalText, _config, _shutdownToken);
            _shutdownToken.ThrowIfCancellationRequested();
            UpdateTranslatedText(result);
            SaveHistory(result, "TooltipSummary");
        }
        catch (OperationCanceledException) when (_shutdownToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            UpdateTranslatedText($"摘要失败: {ex.Message}");
        }
        finally
        {
            if (btn != null)
                btn.IsEnabled = true;
        }
    }

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_translatedText)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(MarkdownOutputSanitizer.RemoveColorTags(_translatedText));

            var btn = this.FindControl<Button>("CopyBtn");
            if (btn != null)
            {
                btn.Content = "已复制 ✔";
                btn.IsEnabled = false;
                await Task.Delay(1500);
                btn.Content = "复制";
                btn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"复制失败: {ex.Message}");
        }
    }

    private void SaveHistory(string result, string mode)
    {
        if (_config == null || string.IsNullOrWhiteSpace(_originalText) || string.IsNullOrWhiteSpace(result))
            return;

        TranslationHistoryService.AddEntry(
            TranslationHistoryService.CreateEntry(_originalText, result, _config, mode),
            _config);
    }

    private async void ReplaceButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_translatedText) || _inputSimulator == null) return;

        // 先隐藏浮窗，再粘贴，避免焦点被浮窗抢走
        Hide();
        Opacity = 0;

        try
        {
            var clipboard = new ClipboardService();
            await clipboard.SetClipboardTextAsync(MarkdownOutputSanitizer.RemoveColorTags(_translatedText));
            await Task.Delay(80);
            await _inputSimulator.SendPasteAsync(null);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"替换失败: {ex.Message}");
        }
        finally
        {
            _isClosing = false;
        }
    }

    // ── 窗口事件 ──────────────────────────────────────────────────────────────

    private void Window_Deactivated(object? sender, EventArgs e)
        => FadeOutAndClose();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            FadeOutAndClose();
        }
    }

    private static void RenderMarkdown(
        TextBlock target,
        string markdown,
        MarkdownRenderOptions options = default)
    {
        if (options == default)
            options = MarkdownRenderOptions.Default;

        target.Text = "";
        target.Inlines?.Clear();

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var colorRenderer = MarkdownColorRendererFactory.Create(options.EnableColorRendering, options.ColorRenderMode);
        // If the model omits semantic tags, add a few display-only highlights without mutating the output text.
        var autoHighlightPlainText = options.EnableColorRendering && !ContainsColorTags(markdown);
        var autoHighlightCount = 0;
        var needsLineBreak = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (options.EnableMathRendering && TryReadMathBlock(lines, ref i, out var formula))
            {
                if (needsLineBreak)
                    target.Inlines?.Add(new LineBreak());

                var mathStyle = new MarkdownInlineStyle(
                    MathBrush,
                    FontWeight.SemiBold,
                    target.FontSize + 1,
                    MathFontFamily);
                AddRun(target, MarkdownMathRenderer.ToDisplayText(formula), mathStyle);
                needsLineBreak = true;
                continue;
            }

            if (needsLineBreak)
                target.Inlines?.Add(new LineBreak());

            var (display, weight, size, indent) = ParseMarkdownLine(lines[i], target.FontSize);
            if (indent > 0)
                target.Inlines?.Add(new Run(new string(' ', indent)));
            AddInlineRuns(
                target,
                display,
                new MarkdownInlineStyle(null, weight, size, null),
                colorRenderer,
                options.EnableMathRendering,
                autoHighlightPlainText,
                ref autoHighlightCount);
            needsLineBreak = true;
        }
    }

    public static (string Display, FontWeight Weight, double FontSize, int Indent) ParseMarkdownLine(
        string line,
        double baseFontSize)
    {
        var text = line.TrimEnd();
        if (string.IsNullOrWhiteSpace(text))
            return ("", FontWeight.Normal, baseFontSize, 0);

        var trimmed = text.TrimStart();
        var headingLevel = 0;
        while (headingLevel < trimmed.Length && headingLevel < 3 && trimmed[headingLevel] == '#')
            headingLevel++;

        if (headingLevel > 0 && headingLevel < trimmed.Length && trimmed[headingLevel] == ' ')
        {
            return (
                trimmed[(headingLevel + 1)..].Trim(),
                FontWeight.Bold,
                headingLevel == 1 ? baseFontSize + 2 : baseFontSize + 1,
                0);
        }

        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            return ("• " + trimmed[2..].Trim(), FontWeight.Normal, baseFontSize, 2);

        var numbered = Regex.Match(trimmed, @"^(\d+)[\.)]\s+(.+)$");
        if (numbered.Success)
            return ($"{numbered.Groups[1].Value}. {numbered.Groups[2].Value}", FontWeight.Normal, baseFontSize, 2);

        return (trimmed, FontWeight.Normal, baseFontSize, 0);
    }

    private static bool TryReadMathBlock(string[] lines, ref int index, out string formula)
    {
        formula = "";
        var trimmed = lines[index].Trim();
        var isDollarBlock = trimmed.StartsWith("$$", StringComparison.Ordinal);
        var isBracketBlock = trimmed.StartsWith(@"\[", StringComparison.Ordinal)
            || trimmed.StartsWith(@"\\[", StringComparison.Ordinal);
        if (!isDollarBlock && !isBracketBlock)
            return false;

        var start = isDollarBlock ? "$$" : trimmed.StartsWith(@"\\[", StringComparison.Ordinal) ? @"\\[" : @"\[";
        var end = isDollarBlock ? "$$" : start == @"\\[" ? @"\\]" : @"\]";
        var current = trimmed[start.Length..];
        var builder = current.EndsWith(end, StringComparison.Ordinal) && current.Length >= end.Length
            ? current[..^end.Length]
            : current;

        while (!current.EndsWith(end, StringComparison.Ordinal) && index + 1 < lines.Length)
        {
            index++;
            current = lines[index].Trim();
            if (current.EndsWith(end, StringComparison.Ordinal))
                builder += "\n" + current[..^end.Length];
            else
                builder += "\n" + current;
        }

        formula = builder.Trim();
        return true;
    }

    private static void AddInlineRuns(
        TextBlock target,
        string text,
        MarkdownInlineStyle baseStyle,
        IMarkdownColorRenderer colorRenderer,
        bool enableMathRendering,
        bool autoHighlightPlainText,
        ref int autoHighlightCount)
    {
        var index = 0;
        while (index < text.Length)
        {
            var next = FindNextSpecial(text, index, enableMathRendering);
            if (next < 0)
            {
                AddPlainTextRuns(
                    target,
                    text[index..],
                    baseStyle,
                    colorRenderer,
                    autoHighlightPlainText,
                    ref autoHighlightCount);
                return;
            }

            if (next > index)
            {
                AddPlainTextRuns(
                    target,
                    text[index..next],
                    baseStyle,
                    colorRenderer,
                    autoHighlightPlainText,
                    ref autoHighlightCount);
                index = next;
            }

            if (TryParseColorTag(text, index, colorRenderer, out var body, out var tagStyle, out var tagLength))
            {
                AddInlineRuns(
                    target,
                    body,
                    baseStyle.Merge(tagStyle),
                    colorRenderer,
                    enableMathRendering,
                    autoHighlightPlainText: false,
                    ref autoHighlightCount);
                index += tagLength;
                continue;
            }

            if (TryParseBold(text, index, out body, out tagLength))
            {
                var boldStyle = new MarkdownInlineStyle(null, FontWeight.Bold, null, null);
                if (colorRenderer.TryGetStyle("emphasis", out var emphasisStyle))
                    boldStyle = emphasisStyle.Merge(boldStyle);

                AddInlineRuns(
                    target,
                    body,
                    baseStyle.Merge(boldStyle),
                    colorRenderer,
                    enableMathRendering,
                    autoHighlightPlainText: false,
                    ref autoHighlightCount);
                index += tagLength;
                continue;
            }

            if (enableMathRendering && TryParseInlineMath(text, index, out var formula, out tagLength))
            {
                var mathStyle = baseStyle.Merge(new MarkdownInlineStyle(
                    MathBrush,
                    FontWeight.SemiBold,
                    baseStyle.FontSize.HasValue ? baseStyle.FontSize.Value + 1 : null,
                    MathFontFamily));
                AddRun(target, MarkdownMathRenderer.ToDisplayText(formula), mathStyle);
                index += tagLength;
                continue;
            }

            AddRun(target, text[index].ToString(), baseStyle);
            index++;
        }
    }

    private static int FindNextSpecial(string text, int start, bool includeMath)
    {
        var next = IndexOfOrMax(text, "**", start);
        next = Math.Min(next, IndexOfOrMax(text, "<", start));
        if (includeMath)
        {
            next = Math.Min(next, IndexOfOrMax(text, "$", start));
            next = Math.Min(next, IndexOfOrMax(text, @"\(", start));
            next = Math.Min(next, IndexOfOrMax(text, @"\\(", start));
            next = Math.Min(next, IndexOfOrMax(text, @"\[", start));
            next = Math.Min(next, IndexOfOrMax(text, @"\\[", start));
        }

        return next == int.MaxValue ? -1 : next;
    }

    private static int IndexOfOrMax(string text, string value, int start)
    {
        var index = text.IndexOf(value, start, StringComparison.Ordinal);
        return index < 0 ? int.MaxValue : index;
    }

    private static bool TryParseBold(string text, int index, out string body, out int length)
    {
        body = "";
        length = 0;
        if (!text.AsSpan(index).StartsWith("**", StringComparison.Ordinal))
            return false;

        var end = text.IndexOf("**", index + 2, StringComparison.Ordinal);
        if (end < 0)
            return false;

        body = text[(index + 2)..end];
        length = end + 2 - index;
        return true;
    }

    private static bool TryParseInlineMath(string text, int index, out string formula, out int length)
    {
        formula = "";
        length = 0;
        if (text[index] == '$')
        {
            if (index + 1 < text.Length && text[index + 1] == '$')
                return TryParseDelimitedMath(text, index, "$$", "$$", out formula, out length);

            var end = text.IndexOf('$', index + 1);
            if (end <= index + 1)
                return false;

            formula = text[(index + 1)..end];
            length = end + 1 - index;
            return true;
        }

        if (TryParseDelimitedMath(text, index, @"\\(", @"\\)", out formula, out length))
            return true;
        if (TryParseDelimitedMath(text, index, @"\(", @"\)", out formula, out length))
            return true;
        if (TryParseDelimitedMath(text, index, @"\\[", @"\\]", out formula, out length))
            return true;
        if (TryParseDelimitedMath(text, index, @"\[", @"\]", out formula, out length))
            return true;

        return false;
    }

    private static bool TryParseDelimitedMath(
        string text,
        int index,
        string open,
        string close,
        out string formula,
        out int length)
    {
        formula = "";
        length = 0;
        if (!text.AsSpan(index).StartsWith(open, StringComparison.Ordinal))
            return false;

        var bodyStart = index + open.Length;
        var bodyEnd = text.IndexOf(close, bodyStart, StringComparison.Ordinal);
        if (bodyEnd <= bodyStart)
            return false;

        formula = text[bodyStart..bodyEnd];
        length = bodyEnd + close.Length - index;
        return true;
    }

    private static bool TryParseColorTag(
        string text,
        int index,
        IMarkdownColorRenderer colorRenderer,
        out string body,
        out MarkdownInlineStyle style,
        out int length)
    {
        body = "";
        style = default;
        length = 0;
        if (text[index] != '<')
            return false;

        var remaining = text[index..];
        var match = Regex.Match(
            remaining,
            @"^<(?<name>hl|mark)\s+type=['""](?<type>[A-Za-z]+)['""]>(?<body>.*?)</\k<name>>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!match.Success)
        {
            match = Regex.Match(
                remaining,
                @"^<(?<type>key|term|accent|warn|warning|ok|success|note|info|emphasis)>(?<body>.*?)</\k<type>>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        if (!match.Success)
            return false;

        body = match.Groups["body"].Value;
        colorRenderer.TryGetStyle(match.Groups["type"].Value, out style);
        length = match.Length;
        return true;
    }

    private static bool ContainsColorTags(string markdown) =>
        Regex.IsMatch(
            markdown,
            @"<(?:(?:key|term|accent|warn|warning|ok|success|note|info|emphasis)\b|(?:hl|mark)\s+type=)",
            RegexOptions.IgnoreCase);

    private static void AddPlainTextRuns(
        TextBlock target,
        string text,
        MarkdownInlineStyle baseStyle,
        IMarkdownColorRenderer colorRenderer,
        bool autoHighlightPlainText,
        ref int autoHighlightCount)
    {
        if (!autoHighlightPlainText
            || baseStyle.Foreground != null
            || autoHighlightCount >= MaxAutoHighlights)
        {
            AddRun(target, text, baseStyle);
            return;
        }

        var index = 0;
        while (index < text.Length)
        {
            if (!TryFindAutoHighlight(text, index, out var match)
                || autoHighlightCount >= MaxAutoHighlights
                || !colorRenderer.TryGetStyle(match.StyleType, out var highlightStyle))
            {
                AddRun(target, text[index..], baseStyle);
                return;
            }

            if (match.Index > index)
                AddRun(target, text[index..match.Index], baseStyle);

            AddRun(target, text.Substring(match.Index, match.Length), baseStyle.Merge(highlightStyle));
            autoHighlightCount++;
            index = match.Index + match.Length;
        }
    }

    private static bool TryFindAutoHighlight(string text, int start, out AutoHighlightMatch result)
    {
        var best = default(AutoHighlightMatch);
        var found = false;

        Consider(AutoWarnRegex, "warn");
        Consider(AutoOkRegex, "ok");
        Consider(AutoTechnicalTermRegex, "term");
        Consider(AutoAcronymRegex, "term");
        Consider(AutoNumberRegex, "key");

        result = best;
        return found;

        void Consider(Regex regex, string styleType)
        {
            var match = regex.Match(text, start);
            if (!match.Success)
                return;

            if (found
                && (match.Index > best.Index
                    || (match.Index == best.Index && match.Length <= best.Length)))
            {
                return;
            }

            best = new AutoHighlightMatch(match.Index, match.Length, styleType);
            found = true;
        }
    }

    private static void AddRun(TextBlock target, string text, MarkdownInlineStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var run = new Run(text)
        {
            FontWeight = style.FontWeight ?? FontWeight.Normal,
            FontSize = style.FontSize ?? target.FontSize,
        };

        if (style.Foreground != null)
            run.Foreground = style.Foreground;
        if (!string.IsNullOrWhiteSpace(style.FontFamily))
            run.FontFamily = new FontFamily(style.FontFamily);

        target.Inlines?.Add(run);
    }

    private readonly record struct AutoHighlightMatch(int Index, int Length, string StyleType);
}
