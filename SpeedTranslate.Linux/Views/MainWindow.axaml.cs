using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.ViewModels;

namespace SpeedTranslate.Linux.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closing += OnClosing;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.RequestSaveAndHide -= OnRequestSaveAndHide;
            _vm.RequestShowError -= OnRequestShowError;
            _vm.RequestShowModelPicker -= OnRequestShowModelPicker;
        }
        _vm = DataContext as MainWindowViewModel;
        if (_vm != null)
        {
            _vm.RequestSaveAndHide += OnRequestSaveAndHide;
            _vm.RequestShowError += OnRequestShowError;
            _vm.RequestShowModelPicker += OnRequestShowModelPicker;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // 关闭按钮点击时隐藏到托盘，主窗仍存活，托盘菜单"退出"才真正关闭。
        if (!_isRealExit)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private bool _isRealExit;

    public void TriggerRealExit()
    {
        _isRealExit = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Hide();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private async void OnRequestSaveAndHide(object? sender, EventArgs e)
    {
        await Task.Yield();
        Hide();
    }

    private async void OnRequestShowError(object? sender, string message)
    {
        await ShowErrorDialogAsync(message);
    }

    private async void OnRequestShowModelPicker(object? sender, List<string> models)
    {
        var picked = await ShowModelPickerAsync(models);
        if (!string.IsNullOrEmpty(picked) && _vm != null)
        {
            _vm.ApplyChosenModelName(picked);
        }
    }

    private async Task ShowErrorDialogAsync(string message)
    {
        var dlg = new Window
        {
            Title = "提示",
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = Avalonia.Media.Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
        };

        var border = new Border
        {
            Background = Avalonia.Media.Brush.Parse("#161224"),
            BorderBrush = Avalonia.Media.Brush.Parse("#2D2547"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
        };
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = Avalonia.Media.Brush.Parse("#E2E8F0"),
            FontSize = 13,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        var ok = new Button
        {
            Content = "确定",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Thickness(20, 6),
        };
        ok.Classes.Add("Primary");
        ok.Click += (_, _) => dlg.Close();
        stack.Children.Add(ok);
        border.Child = stack;
        dlg.Content = border;

        await dlg.ShowDialog(this);
    }

    private Task<string?> ShowModelPickerAsync(List<string> models)
    {
        var tcs = new TaskCompletionSource<string?>();

        var dlg = new Window
        {
            Title = "选择模型",
            Width = 360,
            Height = 420,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SystemDecorations = SystemDecorations.BorderOnly,
            Background = Avalonia.Media.Brushes.Transparent,
        };

        var listBox = new ListBox
        {
            ItemsSource = models,
            Background = Avalonia.Media.Brush.Parse("#1D1A2E"),
            Foreground = Avalonia.Media.Brush.Parse("#E2E8F0"),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var border = new Border
        {
            Background = Avalonia.Media.Brush.Parse("#161224"),
            BorderBrush = Avalonia.Media.Brush.Parse("#2D2547"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        grid.Children.Add(new TextBlock
        {
            Text = "请选择模型",
            Foreground = Avalonia.Media.Brush.Parse("#A855F7"),
            FontWeight = Avalonia.Media.FontWeight.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10),
        });
        Grid.SetRow(listBox, 1);
        grid.Children.Add(listBox);

        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };
        var cancel = new Button { Content = "取消", Padding = new Thickness(20, 6) };
        var ok = new Button { Content = "选用此模型", Padding = new Thickness(20, 6) };
        ok.Classes.Add("Primary");
        cancel.Click += (_, _) => { tcs.TrySetResult(null); dlg.Close(); };
        ok.Click += (_, _) =>
        {
            var chosen = listBox.SelectedItem as string;
            tcs.TrySetResult(chosen);
            dlg.Close();
        };
        listBox.DoubleTapped += (_, _) =>
        {
            var chosen = listBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(chosen))
            {
                tcs.TrySetResult(chosen);
                dlg.Close();
            }
        };
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        Grid.SetRow(btnRow, 2);
        grid.Children.Add(btnRow);

        border.Child = grid;
        dlg.Content = border;
        dlg.Closed += (_, _) => tcs.TrySetResult(null);

        Dispatcher.UIThread.Post(async () => await dlg.ShowDialog(this));
        return tcs.Task;
    }

    private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_vm == null) return;

        var key = e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or
                   Key.LWin or Key.RWin)
            return;

        var modifiers = HotkeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Meta;

        var isFunction = key >= Key.F1 && key <= Key.F24;
        if (modifiers == HotkeyModifiers.None && !isFunction)
            return;

        _vm.ApplyHotkey(modifiers, key.ToString());
    }

    private void TooltipHotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_vm == null) return;

        var key = e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or
                   Key.LWin or Key.RWin)
            return;

        var modifiers = HotkeyModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= HotkeyModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= HotkeyModifiers.Meta;

        var isFunction = key >= Key.F1 && key <= Key.F24;
        if (modifiers == HotkeyModifiers.None && !isFunction)
            return;

        _vm.ApplyTooltipHotkey(modifiers, key.ToString());
    }
}
