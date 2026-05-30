using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.Views;

public partial class TranslationTooltipWindow : Window
{
    private bool _isClosing;
    private string _translatedText = "";
    private string _originalText = "";
    private AppConfig? _config;
    private LLMService? _llmService;
    private InputSimulator? _inputSimulator;

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
        PixelPoint cursorPos)
    {
        _isClosing = false;
        _originalText = originalText;
        _translatedText = translatedText;
        _config = config;
        _llmService = llmService;
        _inputSimulator = inputSimulator;

        // 填充内容
        var modelTag = this.FindControl<TextBlock>("ModelTagText");
        if (modelTag != null) modelTag.Text = $"划词翻译 ({config.SelectedModel})";

        var translated = this.FindControl<TextBlock>("TranslatedTextBlock");
        if (translated != null) translated.Text = translatedText;

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
        var tb = this.FindControl<TextBlock>("TranslatedTextBlock");
        if (tb != null) tb.Text = text;
        RepositionIfNeeded();
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
        if (tb != null) tb.Text = "翻译中...";

        try
        {
            var result = await _llmService.TranslateAsync(_originalText, _config);
            UpdateTranslatedText(result);
        }
        catch (Exception ex)
        {
            UpdateTranslatedText($"翻译失败: {ex.Message}");
        }
    }

    // ── 按钮事件 ──────────────────────────────────────────────────────────────

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => FadeOutAndClose();

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_translatedText)) return;

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(_translatedText);

            var btn = this.FindControl<Button>("CopyBtn");
            if (btn != null)
            {
                btn.Content = "已复制 ✔";
                btn.IsEnabled = false;
                await Task.Delay(1500);
                btn.Content = "📋 复制";
                btn.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"复制失败: {ex.Message}");
        }
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
            await clipboard.SetClipboardTextAsync(_translatedText);
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
}
