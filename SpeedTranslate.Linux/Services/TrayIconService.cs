using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SpeedTranslate.Linux.Services;

public sealed class TrayIconService : IDisposable
{
    private TrayIcon? _trayIcon;
    private readonly Action _onShowMainWindow;
    private readonly Action _onExit;

    public TrayIconService(Action onShowMainWindow, Action onExit)
    {
        _onShowMainWindow = onShowMainWindow;
        _onExit = onExit;
    }

    public void Initialize()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "AxueTranslate",
            Icon = CreateTrayIcon(),
            IsVisible = true,
        };

        _trayIcon.Clicked += (_, _) => _onShowMainWindow();

        var menu = new NativeMenu();
        var showItem = new NativeMenuItem("显示设置");
        showItem.Click += (_, _) => _onShowMainWindow();
        menu.Add(showItem);

        menu.Add(new NativeMenuItemSeparator());

        var exitItem = new NativeMenuItem("退出程序");
        exitItem.Click += (_, _) => _onExit();
        menu.Add(exitItem);

        _trayIcon.Menu = menu;

        var icons = new TrayIcons { _trayIcon };
        if (Application.Current != null)
            TrayIcon.SetIcons(Application.Current, icons);
    }

    /// <summary>
    /// 优先使用和桌面图标一致的 PNG 资源；资源缺失时再动态绘制兜底图标。
    /// </summary>
    private static WindowIcon CreateTrayIcon()
    {
        try
        {
            using var iconStream = AssetLoader.Open(
                new Uri("avares://AxueTranslate/Assets/axue-translate-tray.png"));
            using var assetStream = new MemoryStream();
            iconStream.CopyTo(assetStream);
            assetStream.Position = 0;
            return new WindowIcon(assetStream);
        }
        catch
        {
            // Keep a drawable fallback so the tray still appears if the asset is missing.
        }

        const int size = 64;
        var rt = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (var ctx = rt.CreateDrawingContext())
        {
            var bg = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(255, 99, 102, 241), 0),
                    new GradientStop(Color.FromArgb(255, 168, 85, 247), 1),
                },
            };
            ctx.DrawEllipse(bg, null, new Rect(2, 2, size - 4, size - 4));

            var text = new FormattedText(
                "译",
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Noto Sans CJK SC, Microsoft YaHei, Inter, sans-serif", FontStyle.Normal, FontWeight.Bold),
                size * 0.55,
                Brushes.White);
            var origin = new Point((size - text.Width) / 2, (size - text.Height) / 2);
            ctx.DrawText(text, origin);
        }

        using var ms = new MemoryStream();
        rt.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }
}
