using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Rendering;
using SpeedTranslate.Linux.Services;

namespace SpeedTranslate.Linux.Views;

public sealed class TranslationHistoryWindow : Window
{
    public const string FavoriteStarColor = "#FBBF24";

    private readonly TextBox _searchBox;
    private readonly CheckBox _favoritesOnly;
    private readonly ListBox _listBox;
    private readonly TextBox _detailBox;
    private readonly TextBlock _statusText;
    private List<HistoryListItem> _visibleItems = new();

    public TranslationHistoryWindow()
    {
        Title = "翻译历史";
        Width = 760;
        Height = 560;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        FontFamily = new FontFamily("Noto Sans CJK SC, Microsoft YaHei, WenQuanYi Micro Hei, Inter, sans-serif");

        _searchBox = new TextBox
        {
            Watermark = "搜索原文、译文、模型或语种",
            MinHeight = 34,
        };
        _searchBox.TextChanged += (_, _) => Refresh();

        _favoritesOnly = new CheckBox
        {
            Content = "只看收藏",
            Foreground = Brush.Parse("#CBD5E1"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _favoritesOnly.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty)
                Refresh();
        };

        _listBox = new ListBox
        {
            Background = Brush.Parse("#111827"),
            Foreground = Brush.Parse("#E2E8F0"),
            BorderBrush = Brush.Parse("#2D2547"),
            BorderThickness = new Thickness(1),
            ItemTemplate = new FuncDataTemplate<HistoryListItem>(
                (item, _) => item == null ? null : CreateHistoryListItemView(item)),
        };
        _listBox.SelectionChanged += (_, _) => UpdateDetail();

        _detailBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brush.Parse("#0F172A"),
            Foreground = Brush.Parse("#E2E8F0"),
            BorderBrush = Brush.Parse("#2D2547"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
        };

        _statusText = new TextBlock
        {
            Foreground = Brush.Parse("#64748B"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = BuildContent();
        Refresh();
    }

    private Control BuildContent()
    {
        var root = new Border
        {
            Background = Brush.Parse("#161224"),
            BorderBrush = Brush.Parse("#2D2547"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,12,*,12,Auto"),
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,12,Auto"),
        };
        header.Children.Add(_searchBox);
        Grid.SetColumn(_favoritesOnly, 2);
        header.Children.Add(_favoritesOnly);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("280,12,*"),
        };
        content.Children.Add(_listBox);
        Grid.SetColumn(_detailBox, 2);
        content.Children.Add(_detailBox);
        Grid.SetRow(content, 2);
        grid.Children.Add(content);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };
        buttons.Children.Add(_statusText);
        buttons.Children.Add(CreateButton("复制译文", CopyResult_Click));
        buttons.Children.Add(CreateButton("复制原文", CopySource_Click));
        buttons.Children.Add(CreateButton("收藏/取消", ToggleFavorite_Click));
        buttons.Children.Add(CreateButton("删除", Delete_Click));
        buttons.Children.Add(CreateButton("关闭", (_, _) => Close()));
        Grid.SetRow(buttons, 4);
        grid.Children.Add(buttons);

        root.Child = grid;
        return root;
    }

    private static Button CreateButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button
        {
            Content = text,
            MinHeight = 34,
            Padding = new Thickness(12, 6),
            Background = Brush.Parse("#231F3A"),
            Foreground = Brush.Parse("#E2E8F0"),
            BorderBrush = Brush.Parse("#3D355C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
        };
        button.Click += handler;
        return button;
    }

    private static Control CreateHistoryListItemView(HistoryListItem item)
    {
        var textBlock = new TextBlock
        {
            Foreground = Brush.Parse("#E2E8F0"),
            FontSize = 12,
            LineHeight = 20,
            TextWrapping = TextWrapping.NoWrap,
            Margin = new Thickness(4, 6),
        };

        if (item.Entry.IsFavorite)
        {
            textBlock.Inlines?.Add(new Run("★ ")
            {
                Foreground = Brush.Parse(FavoriteStarColor),
                FontWeight = FontWeight.Bold,
            });
        }

        textBlock.Inlines?.Add(new Run(item.DisplayText));
        return textBlock;
    }

    private void Refresh(string? preferredId = null)
    {
        var entries = TranslationHistoryService.Search(
            _searchBox.Text ?? "",
            _favoritesOnly.IsChecked == true);
        _visibleItems = entries.Select(e => new HistoryListItem(e)).ToList();
        _listBox.ItemsSource = _visibleItems;

        if (_visibleItems.Count == 0)
        {
            _listBox.SelectedIndex = -1;
            _detailBox.Text = "暂无翻译历史。";
            _statusText.Text = "0 条";
            return;
        }

        var preferredIndex = string.IsNullOrWhiteSpace(preferredId)
            ? -1
            : _visibleItems.FindIndex(i => i.Entry.Id == preferredId);
        _listBox.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
        _statusText.Text = $"{_visibleItems.Count} 条";
        UpdateDetail();
    }

    private void UpdateDetail()
    {
        var entry = SelectedEntry;
        if (entry == null)
        {
            _detailBox.Text = "请选择一条历史记录。";
            return;
        }

        _detailBox.Text =
            $"时间: {entry.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n" +
            $"模式: {ModeDisplay(entry.Mode)}\n" +
            $"语种: {entry.TargetLanguage}    风格: {entry.TranslationStyle}\n" +
            $"模型: {entry.ModelProvider} / {entry.ModelName}\n" +
            $"收藏: {(entry.IsFavorite ? "是" : "否")}\n\n" +
            $"原文:\n{entry.SourceText}\n\n" +
            $"译文:\n{entry.ResultText}";
    }

    private TranslationHistoryEntry? SelectedEntry =>
        _listBox.SelectedItem is HistoryListItem item ? item.Entry : null;

    private async void CopyResult_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(MarkdownOutputSanitizer.RemoveColorTags(entry.ResultText));
    }

    private async void CopySource_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null)
            return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(entry.SourceText);
    }

    private void ToggleFavorite_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null)
            return;

        TranslationHistoryService.ToggleFavorite(entry.Id);
        Refresh(entry.Id);
    }

    private void Delete_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var entry = SelectedEntry;
        if (entry == null)
            return;

        TranslationHistoryService.Delete(entry.Id);
        Refresh();
    }

    private static string ModeDisplay(string mode) => mode switch
    {
        "ReplaceTranslation" => "替换翻译",
        "TooltipTranslation" => "浮窗翻译",
        "TooltipSummary" => "浮窗摘要",
        _ => mode,
    };

    private sealed class HistoryListItem
    {
        public HistoryListItem(TranslationHistoryEntry entry)
        {
            Entry = entry;
        }

        public TranslationHistoryEntry Entry { get; }

        public string DisplayText
        {
            get
            {
                var text = string.IsNullOrWhiteSpace(Entry.ResultText) ? Entry.SourceText : Entry.ResultText;
                text = text.Replace('\n', ' ').Replace('\r', ' ');
                if (text.Length > 48)
                    text = text[..48] + "...";

                return $"{Entry.CreatedAt.LocalDateTime:MM-dd HH:mm}  {text}";
            }
        }

        public override string ToString()
            => $"{(Entry.IsFavorite ? "★ " : "")}{DisplayText}";
    }
}
