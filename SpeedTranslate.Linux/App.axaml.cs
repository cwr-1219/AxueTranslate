using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SpeedTranslate.Linux.Services;
using SpeedTranslate.Linux.ViewModels;
using SpeedTranslate.Linux.Views;

namespace SpeedTranslate.Linux;

public partial class App : Application
{
    private MainWindow? _mainWindow;
    private TranslationStatusWindow? _statusWindow;
    private TranslationTooltipWindow? _tooltipWindow;
    private MainWindowViewModel? _vm;
    private GlobalHotkeyService? _hotkey;
    private TrayIconService? _tray;
    private TranslationCoordinator? _coordinator;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DebugLog.Clear();
            DebugLog.Write("[App] Startup");

            // 检测会话类型
            CheckSessionType();

            _vm = new MainWindowViewModel();
            _mainWindow = new MainWindow { DataContext = _vm };
            desktop.MainWindow = _mainWindow;

            _statusWindow = new TranslationStatusWindow();
            _tooltipWindow = new TranslationTooltipWindow();
            _coordinator = new TranslationCoordinator(_vm, _statusWindow);
            _coordinator.SetTooltipWindow(_tooltipWindow);

            // 全局热键
            _hotkey = new GlobalHotkeyService();
            ReregisterHotkey();
            ReregisterTooltipHotkey();
            _vm.HotkeyChanged += (_, _) => ReregisterHotkey();
            _vm.TooltipHotkeyChanged += (_, _) => ReregisterTooltipHotkey();
            _hotkey.Start();

            // 托盘
            _tray = new TrayIconService(
                onShowMainWindow: () => Dispatcher.UIThread.Post(() => _mainWindow?.ShowAndActivate()),
                onExit: () => Dispatcher.UIThread.Post(() => _mainWindow?.TriggerRealExit()));
            _tray.Initialize();

            desktop.ShutdownRequested += (_, _) =>
            {
                _hotkey?.Stop();
                _tray?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ReregisterHotkey()
    {
        if (_hotkey == null || _vm == null || _coordinator == null) return;
        DebugLog.Write($"[App] Register hotkey: {_vm.Hotkey.DisplayText}");
        _hotkey.Register(_vm.Hotkey, () => _coordinator.Trigger());
    }

    private void ReregisterTooltipHotkey()
    {
        if (_hotkey == null || _vm == null || _coordinator == null) return;
        DebugLog.Write($"[App] Register tooltip hotkey: {_vm.TooltipHotkey.DisplayText}");
        _hotkey.Register2(_vm.TooltipHotkey, () => _coordinator.TriggerTooltip());
    }

    private void CheckSessionType()
    {
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("[AxueTranslate] 警告: 检测到 Wayland 会话。");
            Console.WriteLine("[AxueTranslate] 全局快捷键和模拟键盘输入在 Wayland 下可能无法工作。");
            Console.WriteLine("[AxueTranslate] 建议在登录界面切换为 X11 (Xorg) 会话后重新登录。");
        }
    }
}
