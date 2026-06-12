using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.Views;

public partial class ChatDraftWindow : Window
{
    private readonly ClipboardService _clipboard = new();
    private PixelPoint _anchor = new(-9999, -9999);
    private bool _isClosing;

    public ChatDraftWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => RepositionIfNeeded();
    }

    public void ShowDrafts(
        IReadOnlyList<ChatReplyDraft> drafts,
        AppConfig config,
        PixelPoint cursorPos)
    {
        _isClosing = false;
        _anchor = ComputePosition(cursorPos);

        if (this.FindControl<TextBlock>("ModelTagText") is { } modelTag)
            modelTag.Text = $"聊天草稿 ({config.SelectedModel})";

        BuildDraftCards(drafts);
        ApplyLayout(drafts);

        Position = new PixelPoint(-9999, -9999);
        Opacity = 0;
        Show();
        Activate();

        DispatcherTimer.RunOnce(() =>
        {
            RepositionIfNeeded();
            FadeIn();
        }, TimeSpan.FromMilliseconds(30));
    }

    private void BuildDraftCards(IReadOnlyList<ChatReplyDraft> drafts)
    {
        if (this.FindControl<StackPanel>("DraftListPanel") is not { } panel)
            return;

        panel.Children.Clear();
        for (var i = 0; i < drafts.Count; i++)
        {
            panel.Children.Add(CreateDraftCard(drafts[i], i));
        }
    }

    private Control CreateDraftCard(ChatReplyDraft draft, int index)
    {
        var border = new Border
        {
            Background = Brush.Parse(index == 0 ? "#241B3D" : "#161224"),
            BorderBrush = Brush.Parse(index == 0 ? "#A855F7" : "#2D2547"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };

        var title = new TextBlock
        {
            Text = draft.Label,
            Foreground = Brush.Parse("#F8FAFC"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(title);

        var copyButton = new Button
        {
            Content = index == 0 ? "已复制" : "复制",
            MinWidth = 68,
            MinHeight = 32,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Tag = draft.EnglishReply,
        };
        copyButton.Classes.Add("DraftAction");
        copyButton.Click += CopyButton_Click;
        Grid.SetColumn(copyButton, 1);
        grid.Children.Add(copyButton);

        if (!string.IsNullOrWhiteSpace(draft.ChineseIntent))
        {
            var intent = new TextBlock
            {
                Text = draft.ChineseIntent,
                Foreground = Brush.Parse("#94A3B8"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            };
            Grid.SetRow(intent, 1);
            Grid.SetColumnSpan(intent, 2);
            grid.Children.Add(intent);
        }

        var reply = new TextBlock
        {
            Text = draft.EnglishReply,
            Foreground = Brush.Parse("#E2E8F0"),
            FontSize = 14,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(reply, 2);
        Grid.SetColumnSpan(reply, 2);
        grid.Children.Add(reply);

        border.Child = grid;
        return border;
    }

    private async void CopyButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string text || string.IsNullOrWhiteSpace(text))
            return;

        await _clipboard.SetClipboardTextAsync(text);
        var original = button.Content;
        button.Content = "已复制";
        button.IsEnabled = false;
        await Task.Delay(1200);
        button.Content = original?.ToString() == "已复制" ? "已复制" : "复制";
        button.IsEnabled = true;
    }

    private static PixelPoint ComputePosition(PixelPoint cursor) =>
        new(cursor.X + 12, cursor.Y + 18);

    private void ApplyLayout(IReadOnlyList<ChatReplyDraft> drafts)
    {
        var maxReplyLength = drafts.Count == 0 ? 0 : drafts.Max(d => d.EnglishReply.Length);
        Width = maxReplyLength > 260 ? 620 : 520;

        if (this.FindControl<ScrollViewer>("DraftListScrollViewer") is { } scrollViewer)
        {
            var workArea = Screens?.ScreenFromPoint(_anchor)?.WorkingArea
                ?? Screens?.Primary?.WorkingArea
                ?? new PixelRect(0, 0, 1366, 768);
            scrollViewer.MaxHeight = Math.Min(520, Math.Max(300, workArea.Height - 220));
        }
    }

    private void RepositionIfNeeded()
    {
        if (_anchor.X == -9999) return;

        var screen = Screens?.ScreenFromPoint(_anchor) ?? Screens?.Primary;
        if (screen == null) return;

        var workArea = screen.WorkingArea;
        var w = (int)(Bounds.Width == 0 ? Width : Bounds.Width + 20);
        var h = (int)(Bounds.Height == 0 ? 180 : Bounds.Height + 20);

        var x = _anchor.X;
        var y = _anchor.Y;

        if (x + w > workArea.X + workArea.Width) x = _anchor.X - w - 10;
        if (y + h > workArea.Y + workArea.Height) y = _anchor.Y - h - 10;
        if (x < workArea.X) x = workArea.X + 10;
        if (y < workArea.Y) y = workArea.Y + 10;

        Position = new PixelPoint(x, y);
    }

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

    private void CloseButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => FadeOutAndClose();

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
