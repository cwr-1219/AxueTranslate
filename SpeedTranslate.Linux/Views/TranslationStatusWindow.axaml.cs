using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.Views;

public partial class TranslationStatusWindow : Window
{
    private TextBlock? _statusText;
    private Avalonia.Controls.Shapes.Path? _spinnerPath;

    public TranslationStatusWindow()
    {
        InitializeComponent();
        _statusText = this.FindControl<TextBlock>("StatusTextBlock");
        _spinnerPath = this.FindControl<Avalonia.Controls.Shapes.Path>("SpinnerPath");
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // 打开后立即设置 X11 input shape 让窗口对鼠标透明
        TryMakeClickThrough();
    }

    private void TryMakeClickThrough()
    {
        try
        {
            var handle = TryGetX11Handle();
            if (handle != IntPtr.Zero)
            {
                X11InputShape.MakeClickThrough(handle);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"鼠标穿透设置失败: {ex.Message}");
        }
    }

    private IntPtr TryGetX11Handle()
    {
        // Avalonia 11 上 PlatformImpl.Handle.Handle 是 X11 Window XID
        var impl = (this as Window)?.TryGetPlatformHandle();
        return impl?.Handle ?? IntPtr.Zero;
    }

    /// <summary>
    /// 在指定屏幕坐标（鼠标位置 + 偏移）显示，并重置为"翻译中"状态。
    /// </summary>
    public void ShowAtCursor(int cursorX, int cursorY)
    {
        Reset();
        // 鼠标右下方 25 像素
        Position = new PixelPoint(cursorX + 25, cursorY + 25);
        Opacity = 0;
        Show();
        // 简单淡入
        DispatcherTimer.RunOnce(() => Opacity = 1, TimeSpan.FromMilliseconds(20));
        // 显示后再次确认 input shape 已应用（部分窗口管理器在 Map 后才生效）
        DispatcherTimer.RunOnce(TryMakeClickThrough, TimeSpan.FromMilliseconds(100));
    }

    public static (int X, int Y) GetCursorPos() => X11Mouse.GetPosition();

    private void Reset()
    {
        if (_statusText != null)
        {
            _statusText.Text = "AI 正在翻译中...";
            _statusText.Foreground = Brush.Parse("#E2E8F0");
        }
        if (_spinnerPath != null)
        {
            _spinnerPath.IsVisible = true;
        }
    }

    public void ShowError(string errorMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_spinnerPath != null) _spinnerPath.IsVisible = false;
            if (_statusText != null)
            {
                _statusText.Text = errorMessage;
                _statusText.Foreground = Brushes.Tomato;
            }
            DispatcherTimer.RunOnce(HideWithFade, TimeSpan.FromMilliseconds(1500));
        });
    }

    public void HideWithFade()
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // 简单线性淡出
            for (var op = 1.0; op > 0; op -= 0.1)
            {
                Opacity = op;
                await Task.Delay(20);
            }
            Hide();
            Opacity = 0;
        });
    }
}
