using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Rendering;
using SpeedTranslate.Linux.Services;
using SpeedTranslate.Linux.Views;

var tests = new (string Name, Func<Task> Run)[]
{
    ("LLMService.TranslateAsync honors an already-canceled token", TestTranslateAsyncHonorsCanceledToken),
    ("LLMService.TranslateAsync cancels an in-flight request", TestTranslateAsyncCancelsInFlightRequest),
    ("TranslationCoordinator exposes a shutdown cancellation hook", TestCoordinatorExposesCancelPendingWork),
    ("TranslationCoordinator.CancelPendingWork returns promptly", TestCancelPendingWorkReturnsPromptly),
    ("AppConfig keeps automatic summary opt-in", TestSummaryConfigDefaults),
    ("MainWindowViewModel runtime config reflects unsaved auto-summary toggle", TestRuntimeConfigReflectsAutoSummaryToggle),
    ("AppConfig keeps markdown rendering defaults", TestMarkdownRenderingConfigDefaults),
    ("AppConfig keeps translation history defaults", TestTranslationHistoryConfigDefaults),
    ("History hotkey is configurable and registered", TestHistoryHotkeyConfiguration),
    ("AppConfig keeps chat draft defaults", TestChatDraftConfigDefaults),
    ("Chat draft hotkey is configurable and registered", TestChatDraftHotkeyConfiguration),
    ("AppConfig keeps context rewrite defaults", TestContextRewriteConfigDefaults),
    ("Context rewrite hotkey is configurable and registered", TestContextRewriteHotkeyConfiguration),
    ("LLMService parses chat reply draft JSON", TestChatReplyDraftParsing),
    ("LLMService generates context rewrite drafts with cached context payload", TestContextRewriteDraftGeneration),
    ("LLMService falls back for non-JSON chat reply output", TestChatReplyDraftFallback),
    ("ChatDraftWindow exposes candidate copy UI", TestChatDraftWindowUi),
    ("TranslationHistoryWindow exposes yellow favorite star color", TestHistoryFavoriteStarColor),
    ("TranslationHistoryService stores and searches local entries", TestTranslationHistoryServiceStoresAndSearches),
    ("TranslationHistoryService prunes old ordinary history but keeps favorites first", TestTranslationHistoryPruning),
    ("TranslationHistoryService tolerates corrupted history file", TestTranslationHistoryCorruptFile),
    ("TranslationCoordinator auto summary policy uses the configured threshold", TestAutoSummaryPolicy),
    ("TranslationTooltipWindow parses simple Markdown lines", TestMarkdownParsing),
    ("TranslationTooltipWindow controls have clipping-safe dimensions", TestTooltipControlDimensions),
    ("MarkdownMathRenderer converts common LaTeX syntax", TestMarkdownMathRendering),
    ("TranslationTooltipWindow parses escaped inline math delimiters", TestEscapedInlineMathParsing),
    ("TranslationTooltipWindow renders escaped math instead of raw LaTeX", TestTooltipRendersEscapedMath),
    ("TranslationTooltipWindow applies semantic highlight fallback", TestTooltipSemanticHighlightFallback),
    ("Markdown color renderer factory respects enable flag", TestMarkdownColorRendererFactory),
    ("Markdown color tags are stripped for plain output", TestMarkdownOutputSanitizer),
    ("LLMService adds semantic color tag prompt only when enabled", TestMarkdownRenderingPrompt),
    ("LLMService builds semantic color prompt from background thread", TestMarkdownRenderingPromptFromBackgroundThread),
    ("LLMService uses a longer timeout for long text", TestLongTextUsesLongerTimeout),
    ("TranslationTooltipWindow keeps short text compact and expands long text", TestTooltipLayoutExpandsForLongText),
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.GetType().Name}: {ex.Message}");
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static async Task TestTranslateAsyncHonorsCanceledToken()
{
    var service = new LLMService();
    var config = new AppConfig
    {
        SelectedModel = "Custom",
        CustomUrl = "https://example.invalid/v1",
        CustomApiKey = "test-key",
        CustomModel = "test-model",
    };
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    try
    {
        await service.TranslateAsync("hello", config, cts.Token);
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new Exception("Expected OperationCanceledException.");
}

static async Task TestTranslateAsyncCancelsInFlightRequest()
{
    var handler = new WaitingHttpMessageHandler();
    using var httpClient = new HttpClient(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
    var service = new LLMService(httpClient);
    var config = new AppConfig
    {
        SelectedModel = "Custom",
        CustomUrl = "https://example.invalid/v1",
        CustomApiKey = "test-key",
        CustomModel = "test-model",
    };

    using var cts = new CancellationTokenSource();
    var translateTask = service.TranslateAsync("hello", config, cts.Token);

    await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
    cts.Cancel();

    var completed = await Task.WhenAny(translateTask, Task.Delay(TimeSpan.FromSeconds(3)));
    if (completed != translateTask)
        throw new Exception("TranslateAsync did not stop promptly after cancellation.");

    try
    {
        await translateTask;
    }
    catch (OperationCanceledException)
    {
        return;
    }

    throw new Exception("Expected OperationCanceledException.");
}

static Task TestCoordinatorExposesCancelPendingWork()
{
    var method = typeof(TranslationCoordinator).GetMethod(
        "CancelPendingWork",
        Type.EmptyTypes);

    if (method == null)
        throw new Exception("CancelPendingWork() was not found.");

    if (method.ReturnType != typeof(void))
        throw new Exception("CancelPendingWork() must return void.");

    return Task.CompletedTask;
}

static async Task TestCancelPendingWorkReturnsPromptly()
{
    var coordinator = new TranslationCoordinator(null!, null!);
    var field = typeof(TranslationCoordinator).GetField(
        "_shutdownCts",
        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var cts = field?.GetValue(coordinator) as CancellationTokenSource;
    if (cts == null)
        throw new Exception("Could not inspect TranslationCoordinator shutdown token source.");

    using var registration = cts.Token.Register(() => Thread.Sleep(1500));

    var sw = Stopwatch.StartNew();
    coordinator.CancelPendingWork();
    sw.Stop();

    if (sw.Elapsed > TimeSpan.FromMilliseconds(300))
        throw new Exception($"CancelPendingWork blocked for {sw.Elapsed.TotalMilliseconds:0}ms.");

    var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
    while (!cts.IsCancellationRequested && DateTime.UtcNow < deadline)
        await Task.Delay(20);

    if (!cts.IsCancellationRequested)
        throw new Exception("Shutdown token was not canceled.");
}

static Task TestSummaryConfigDefaults()
{
    var config = new AppConfig();
    if (config.EnableAutoSummary)
        throw new Exception("Automatic summary should be opt-in.");

    if (config.AutoSummaryMinLength != 1800)
        throw new Exception($"Expected default summary threshold 1800, got {config.AutoSummaryMinLength}.");

    return Task.CompletedTask;
}

static Task TestRuntimeConfigReflectsAutoSummaryToggle()
{
    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    var tempConfigHome = Path.Combine(Path.GetTempPath(), "axue-config-test-" + Guid.NewGuid());
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempConfigHome);

    try
    {
        var vm = new SpeedTranslate.Linux.ViewModels.MainWindowViewModel();
        vm.EnableAutoSummary = true;
        if (!vm.CurrentConfig.EnableAutoSummary)
            throw new Exception("Expected runtime config to reflect enabled auto summary.");

        vm.EnableAutoSummary = false;
        if (vm.CurrentConfig.EnableAutoSummary)
            throw new Exception("Expected runtime config to reflect disabled auto summary before saving.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
        if (Directory.Exists(tempConfigHome))
            Directory.Delete(tempConfigHome, recursive: true);
    }

    return Task.CompletedTask;
}

static Task TestMarkdownRenderingConfigDefaults()
{
    var config = new AppConfig();
    if (!config.EnableMarkdownMathRendering)
        throw new Exception("Math rendering should be enabled by default.");

    if (config.EnableMarkdownColorRendering)
        throw new Exception("Semantic color rendering should be opt-in.");

    if (config.MarkdownColorRenderMode != MarkdownColorRenderModes.SemanticTags)
        throw new Exception($"Unexpected default color render mode: {config.MarkdownColorRenderMode}.");

    return Task.CompletedTask;
}

static Task TestTranslationHistoryConfigDefaults()
{
    var config = new AppConfig();
    if (!config.EnableTranslationHistory)
        throw new Exception("Translation history should be enabled by default.");

    if (config.HistoryRetentionDays != 30)
        throw new Exception($"Expected history retention 30 days, got {config.HistoryRetentionDays}.");

    if (config.MaxHistoryItems != 500)
        throw new Exception($"Expected max history items 500, got {config.MaxHistoryItems}.");

    if (config.HistoryHotkey.DisplayText != "Ctrl + Alt + H")
        throw new Exception($"Expected default history hotkey Ctrl + Alt + H, got {config.HistoryHotkey.DisplayText}.");

    return Task.CompletedTask;
}

static Task TestHistoryHotkeyConfiguration()
{
    var method = typeof(GlobalHotkeyService).GetMethod("Register3");
    if (method == null)
        throw new Exception("Expected GlobalHotkeyService.Register3 for history hotkey.");

    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    var tempConfigHome = Path.Combine(Path.GetTempPath(), "axue-history-hotkey-test-" + Guid.NewGuid());
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempConfigHome);

    try
    {
        var vm = new SpeedTranslate.Linux.ViewModels.MainWindowViewModel();
        vm.ApplyHistoryHotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, "H");
        if (vm.HistoryHotkeyDisplay != "Ctrl + Shift + H")
            throw new Exception($"Expected history hotkey display to update, got {vm.HistoryHotkeyDisplay}.");

        if (vm.CurrentConfig.HistoryHotkey.DisplayText != "Ctrl + Shift + H")
            throw new Exception("Expected runtime config to reflect history hotkey.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
        if (Directory.Exists(tempConfigHome))
            Directory.Delete(tempConfigHome, recursive: true);
    }

    var xaml = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/MainWindow.axaml"));
    if (!xaml.Contains("HistoryHotkeyTextBox") || !xaml.Contains("HistoryHotkeyDisplay"))
        throw new Exception("Settings window should expose history hotkey input.");

    var appCode = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/App.axaml.cs"));
    if (!appCode.Contains("ReregisterHistoryHotkey") || !appCode.Contains("ShowHistoryWindow"))
        throw new Exception("App should register a global history hotkey that opens history.");

    return Task.CompletedTask;
}

static Task TestChatDraftConfigDefaults()
{
    var config = new AppConfig();
    if (config.ChatReplyTone != "PoliteFriendly")
        throw new Exception($"Expected default chat reply tone PoliteFriendly, got {config.ChatReplyTone}.");

    if (config.ChatDraftHotkey.DisplayText != "Ctrl + Alt + R")
        throw new Exception($"Expected default chat draft hotkey Ctrl + Alt + R, got {config.ChatDraftHotkey.DisplayText}.");

    return Task.CompletedTask;
}

static Task TestChatDraftHotkeyConfiguration()
{
    var method = typeof(GlobalHotkeyService).GetMethod("Register4");
    if (method == null)
        throw new Exception("Expected GlobalHotkeyService.Register4 for chat draft hotkey.");

    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    var tempConfigHome = Path.Combine(Path.GetTempPath(), "axue-chat-hotkey-test-" + Guid.NewGuid());
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempConfigHome);

    try
    {
        var vm = new SpeedTranslate.Linux.ViewModels.MainWindowViewModel();
        vm.ApplyChatDraftHotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, "R");
        vm.ChatReplyToneIndex = 2;

        if (vm.ChatDraftHotkeyDisplay != "Ctrl + Shift + R")
            throw new Exception($"Expected chat draft hotkey display to update, got {vm.ChatDraftHotkeyDisplay}.");

        if (vm.CurrentConfig.ChatDraftHotkey.DisplayText != "Ctrl + Shift + R")
            throw new Exception("Expected runtime config to reflect chat draft hotkey.");

        if (vm.CurrentConfig.ChatReplyTone != "ConciseDirect")
            throw new Exception("Expected runtime config to reflect chat reply tone.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
        if (Directory.Exists(tempConfigHome))
            Directory.Delete(tempConfigHome, recursive: true);
    }

    var xaml = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/MainWindow.axaml"));
    if (!xaml.Contains("ChatDraftHotkeyTextBox") || !xaml.Contains("ChatReplyToneOptions"))
        throw new Exception("Settings window should expose chat draft controls.");

    var appCode = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/App.axaml.cs"));
    if (!appCode.Contains("ReregisterChatDraftHotkey") || !appCode.Contains("TriggerChatDraft"))
        throw new Exception("App should register a global chat draft hotkey.");

    var coordinatorCode = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Services/TranslationCoordinator.cs"));
    if (!coordinatorCode.Contains("GetPrimarySelectionAsync") || !coordinatorCode.Contains("ChatReplyDraft"))
        throw new Exception("Chat draft flow should read the selected X11 PRIMARY text and save history.");

    return Task.CompletedTask;
}

static Task TestContextRewriteConfigDefaults()
{
    var config = new AppConfig();
    if (config.ContextRewriteHotkey.DisplayText != "Ctrl + Alt + E")
        throw new Exception(
            $"Expected default context rewrite hotkey Ctrl + Alt + E, got {config.ContextRewriteHotkey.DisplayText}.");

    return Task.CompletedTask;
}

static Task TestContextRewriteHotkeyConfiguration()
{
    var method = typeof(GlobalHotkeyService).GetMethod("Register5");
    if (method == null)
        throw new Exception("Expected GlobalHotkeyService.Register5 for context rewrite hotkey.");

    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    var tempConfigHome = Path.Combine(Path.GetTempPath(), "axue-context-hotkey-test-" + Guid.NewGuid());
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempConfigHome);

    try
    {
        var vm = new SpeedTranslate.Linux.ViewModels.MainWindowViewModel();
        vm.ApplyContextRewriteHotkey(HotkeyModifiers.Control | HotkeyModifiers.Shift, "E");

        if (vm.ContextRewriteHotkeyDisplay != "Ctrl + Shift + E")
            throw new Exception(
                $"Expected context rewrite hotkey display to update, got {vm.ContextRewriteHotkeyDisplay}.");

        if (vm.CurrentConfig.ContextRewriteHotkey.DisplayText != "Ctrl + Shift + E")
            throw new Exception("Expected runtime config to reflect context rewrite hotkey.");
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
        if (Directory.Exists(tempConfigHome))
            Directory.Delete(tempConfigHome, recursive: true);
    }

    var xaml = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/MainWindow.axaml"));
    if (!xaml.Contains("ContextRewriteHotkeyTextBox") || !xaml.Contains("ContextRewriteHotkeyDisplay"))
        throw new Exception("Settings window should expose context rewrite controls.");

    var appCode = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/App.axaml.cs"));
    if (!appCode.Contains("ReregisterContextRewriteHotkey") || !appCode.Contains("TriggerContextRewrite"))
        throw new Exception("App should register a global context rewrite hotkey.");

    var coordinatorCode = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Services/TranslationCoordinator.cs"));
    if (!coordinatorCode.Contains("_cachedChatContext") ||
        !coordinatorCode.Contains("请先选中聊天记录并按聊天草稿热键") ||
        !coordinatorCode.Contains("请先选中你的中文回复"))
    {
        throw new Exception("Context rewrite flow should cache context and handle missing input states.");
    }

    return Task.CompletedTask;
}

static Task TestChatReplyDraftParsing()
{
    var drafts = LLMService.ParseChatReplyDrafts("""
        {"drafts":[
          {"label":"最佳回复","chineseIntent":"表达赞同","englishReply":"That sounds good to me."},
          {"label":"更轻松","chineseIntent":"轻松回应","englishReply":"Yeah, that sounds good."},
          {"label":"更稳妥","chineseIntent":"礼貌确认","englishReply":"That works for me, thank you."}
        ]}
        """);

    if (drafts.Count != 3)
        throw new Exception($"Expected 3 parsed drafts, got {drafts.Count}.");

    if (drafts[0].Label != "最佳回复" || drafts[0].ChineseIntent != "表达赞同")
        throw new Exception("Expected draft metadata to be parsed.");

    if (drafts[1].EnglishReply != "Yeah, that sounds good.")
        throw new Exception("Expected English reply to be parsed.");

    return Task.CompletedTask;
}

static async Task TestContextRewriteDraftGeneration()
{
    var handler = new RespondingHttpMessageHandler("""
        {"choices":[{"message":{"content":"{\"drafts\":[{\"label\":\"最佳回复\",\"chineseIntent\":\"表达愿意稍后见\",\"englishReply\":\"Sure, see you later.\"},{\"label\":\"自然一点\",\"chineseIntent\":\"轻松回应\",\"englishReply\":\"Yeah, see you later.\"},{\"label\":\"礼貌一点\",\"chineseIntent\":\"礼貌确认\",\"englishReply\":\"Sure, I'll see you later.\"}]}"}}]}
        """);
    using var httpClient = new HttpClient(handler);
    var service = new LLMService(httpClient);
    var config = new AppConfig
    {
        SelectedModel = "Custom",
        CustomUrl = "https://example.invalid/v1",
        CustomApiKey = "test-key",
        CustomModel = "test-model",
    };

    var drafts = await service.GenerateContextRewriteDraftsAsync(
        "UK friend: See you later.",
        "好的，晚点见。",
        config);

    if (drafts.Count != 3)
        throw new Exception($"Expected 3 context rewrite drafts, got {drafts.Count}.");

    if (drafts[0].Label != "最佳回复" || drafts[0].EnglishReply != "Sure, see you later.")
        throw new Exception("Expected context rewrite draft to be parsed.");

    var requestJson = JsonDocument.Parse(handler.LastRequestBody);
    var userContent = requestJson.RootElement
        .GetProperty("messages")[1]
        .GetProperty("content")
        .GetString() ?? "";

    if (!userContent.Contains("Recent chat context:") ||
        !userContent.Contains("UK friend: See you later.") ||
        !userContent.Contains("Chinese draft reply:") ||
        !userContent.Contains("好的，晚点见。"))
    {
        throw new Exception("Expected context rewrite request to include both context and Chinese draft reply.");
    }
}

static Task TestChatReplyDraftFallback()
{
    var drafts = LLMService.ParseChatReplyDrafts("Sure, that sounds good to me.");
    if (drafts.Count != 1)
        throw new Exception($"Expected one fallback draft, got {drafts.Count}.");

    if (drafts[0].Label != "最佳回复" || drafts[0].EnglishReply != "Sure, that sounds good to me.")
        throw new Exception("Expected fallback draft to keep the model output.");

    return Task.CompletedTask;
}

static Task TestChatDraftWindowUi()
{
    var xaml = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/ChatDraftWindow.axaml"));
    if (!xaml.Contains("DraftListPanel") || !xaml.Contains("第一条最佳回复已复制到剪贴板"))
        throw new Exception("Chat draft window should expose a candidate list and clipboard hint.");

    var source = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/ChatDraftWindow.axaml.cs"));
    if (!source.Contains("CopyButton_Click") ||
        !source.Contains("SetClipboardTextAsync") ||
        !source.Contains("clipboardHint") ||
        !source.Contains("title"))
    {
        throw new Exception("Chat draft window should support copying candidates and custom context rewrite labels.");
    }

    return Task.CompletedTask;
}

static Task TestHistoryFavoriteStarColor()
{
    if (TranslationHistoryWindow.FavoriteStarColor != "#FBBF24")
        throw new Exception($"Expected yellow favorite star color, got {TranslationHistoryWindow.FavoriteStarColor}.");

    var source = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/TranslationHistoryWindow.cs"));
    if (!source.Contains("new Run(\"★ \")") || !source.Contains("FavoriteStarColor"))
        throw new Exception("History window should render the favorite star as a separate colored run.");

    return Task.CompletedTask;
}

static Task TestTranslationHistoryServiceStoresAndSearches() =>
    WithTempConfigHome(() =>
    {
        var config = new AppConfig
        {
            SelectedModel = "Custom",
            CustomModel = "test-model",
            TargetLanguage = "Chinese",
            TranslationStyle = "Business",
            EnableTranslationHistory = true,
        };

        var entry = TranslationHistoryService.CreateEntry(
            "hello world",
            "你好 <key>世界</key>",
            config,
            "TooltipTranslation");
        TranslationHistoryService.AddEntry(entry, config);

        var entries = TranslationHistoryService.LoadEntries();
        if (entries.Count != 1)
            throw new Exception($"Expected one history entry, got {entries.Count}.");

        if (entries[0].ModelName != "test-model" || entries[0].Mode != "TooltipTranslation")
            throw new Exception("History entry did not keep model metadata.");

        var matches = TranslationHistoryService.Search("hello", favoritesOnly: false);
        if (matches.Count != 1)
            throw new Exception("Expected source text search to find the entry.");

        if (!TranslationHistoryService.ToggleFavorite(entries[0].Id))
            throw new Exception("Expected favorite toggle to succeed.");

        var favorites = TranslationHistoryService.Search("", favoritesOnly: true);
        if (favorites.Count != 1 || !favorites[0].IsFavorite)
            throw new Exception("Expected favorite filter to return the toggled entry.");

        if (!TranslationHistoryService.Delete(entries[0].Id))
            throw new Exception("Expected delete to succeed.");

        if (TranslationHistoryService.LoadEntries().Count != 0)
            throw new Exception("Expected history to be empty after delete.");

        config.EnableTranslationHistory = false;
        TranslationHistoryService.AddEntry(
            TranslationHistoryService.CreateEntry("disabled", "disabled", config, "ReplaceTranslation"),
            config);
        if (TranslationHistoryService.LoadEntries().Count != 0)
            throw new Exception("Disabled history should not write entries.");

        return Task.CompletedTask;
    });

static Task TestTranslationHistoryPruning()
{
    var config = new AppConfig
    {
        HistoryRetentionDays = 30,
        MaxHistoryItems = 2,
    };
    var now = DateTimeOffset.Now;
    var oldOrdinary = new TranslationHistoryEntry
    {
        Id = "old",
        CreatedAt = now.AddDays(-60),
        SourceText = "old ordinary",
        ResultText = "old ordinary",
    };
    var oldFavorite = new TranslationHistoryEntry
    {
        Id = "favorite",
        CreatedAt = now.AddDays(-90),
        SourceText = "old favorite",
        ResultText = "old favorite",
        IsFavorite = true,
    };
    var recentOne = new TranslationHistoryEntry
    {
        Id = "recent-one",
        CreatedAt = now.AddDays(-1),
        SourceText = "recent one",
        ResultText = "recent one",
    };
    var recentTwo = new TranslationHistoryEntry
    {
        Id = "recent-two",
        CreatedAt = now,
        SourceText = "recent two",
        ResultText = "recent two",
    };

    var pruned = TranslationHistoryService.Prune(
        new[] { oldOrdinary, oldFavorite, recentOne, recentTwo },
        config);

    if (pruned.Count != 2)
        throw new Exception($"Expected two pruned entries, got {pruned.Count}.");

    if (!ContainsHistoryId(pruned, "favorite"))
        throw new Exception("Expected old favorite to survive retention pruning.");

    if (ContainsHistoryId(pruned, "old"))
        throw new Exception("Expected old ordinary entry to be removed.");

    if (!ContainsHistoryId(pruned, "recent-two"))
        throw new Exception("Expected newest ordinary entry to survive max-item pruning.");

    return Task.CompletedTask;
}

static Task TestTranslationHistoryCorruptFile() =>
    WithTempConfigHome(() =>
    {
        Directory.CreateDirectory(ConfigManager.ConfigDir);
        File.WriteAllText(TranslationHistoryService.HistoryPath, "{ broken json");

        var entries = TranslationHistoryService.LoadEntries();
        if (entries.Count != 0)
            throw new Exception("Corrupted history should load as empty.");

        if (!File.Exists(ConfigManager.ErrorLogPath))
            throw new Exception("Corrupted history should write an error log.");

        return Task.CompletedTask;
    });

static Task TestAutoSummaryPolicy()
{
    var config = new AppConfig
    {
        EnableAutoSummary = true,
        AutoSummaryMinLength = 10,
    };

    if (TranslationCoordinator.ShouldAutoSummarize(new string('x', 299), config))
        throw new Exception("Threshold should be clamped to at least 300 characters.");

    if (!TranslationCoordinator.ShouldAutoSummarize(new string('x', 300), config))
        throw new Exception("Expected long text to trigger automatic summary.");

    config.EnableAutoSummary = false;
    if (TranslationCoordinator.ShouldAutoSummarize(new string('x', 1000), config))
        throw new Exception("Disabled automatic summary should not trigger.");

    return Task.CompletedTask;
}

static Task TestMarkdownParsing()
{
    var heading = TranslationTooltipWindow.ParseMarkdownLine("## 核心结论", 12);
    if (heading.Display != "核心结论" || heading.Weight != FontWeight.Bold || heading.FontSize <= 12)
        throw new Exception($"Unexpected heading parse result: {heading}.");

    var bullet = TranslationTooltipWindow.ParseMarkdownLine("- **重点** 内容", 12);
    if (bullet.Display != "• **重点** 内容" || bullet.Indent == 0)
        throw new Exception($"Unexpected bullet parse result: {bullet}.");

    var numbered = TranslationTooltipWindow.ParseMarkdownLine("2) 第二点", 12);
    if (numbered.Display != "2. 第二点" || numbered.Indent == 0)
        throw new Exception($"Unexpected numbered parse result: {numbered}.");

    return Task.CompletedTask;
}

static Task TestTooltipControlDimensions()
{
    var xaml = File.ReadAllText(GetRepoPath("SpeedTranslate.Linux/Views/TranslationTooltipWindow.axaml"));
    if (!xaml.Contains("Noto Sans CJK SC"))
        throw new Exception("Tooltip should use a CJK-first font family to avoid Linux fallback clipping.");

    var tooSmallControlHeight = Regex.Match(xaml, @"(?<!Line)(?:Height|MinHeight)=""(?:1\d|2[0-9])""");
    if (tooSmallControlHeight.Success)
        throw new Exception($"Tooltip still contains a clipping-prone control height: {tooSmallControlHeight.Value}.");

    if (!xaml.Contains("MinHeight=\"34\""))
        throw new Exception("Tooltip action controls should keep at least 34px vertical space for CJK text.");

    return Task.CompletedTask;
}

static Task TestMarkdownMathRendering()
{
    var display = MarkdownMathRenderer.ToDisplayText(@"\frac{a_1}{b^2} + \alpha \times x_n");
    if (!display.Contains("a₁/b²"))
        throw new Exception($"Expected fraction with sub/superscripts, got: {display}");

    if (!display.Contains("α × xₙ"))
        throw new Exception($"Expected Greek letter and operator rendering, got: {display}");

    var escapedDisplay = MarkdownMathRenderer.ToDisplayText(@"\\frac{a_1}{b^2} + \\alpha");
    if (!escapedDisplay.Contains("a₁/b²") || !escapedDisplay.Contains("α"))
        throw new Exception($"Expected doubled backslash LaTeX to render, got: {escapedDisplay}");

    var delimitedDisplay = MarkdownMathRenderer.ToDisplayText(@"\\( S_n = \\{w_i\\}_{i=1}^n \\)");
    if (delimitedDisplay.Contains(@"\(") || !delimitedDisplay.Contains("Sₙ"))
        throw new Exception($"Expected escaped math delimiters to be stripped, got: {delimitedDisplay}");

    return Task.CompletedTask;
}

static Task TestEscapedInlineMathParsing()
{
    var method = typeof(TranslationTooltipWindow).GetMethod(
        "TryParseInlineMath",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    if (method == null)
        throw new Exception("TryParseInlineMath was not found.");

    object?[] args = { @"\\(a_1 + b^2\\)", 0, "", 0 };
    var success = (bool)(method.Invoke(null, args) ?? false);
    if (!success)
        throw new Exception("Expected escaped inline math delimiters to parse.");

    if ((string?)args[2] != "a_1 + b^2")
        throw new Exception($"Unexpected parsed formula: {args[2]}");

    if ((int)(args[3] ?? 0) != @"\\(a_1 + b^2\\)".Length)
        throw new Exception($"Unexpected parsed length: {args[3]}");

    return Task.CompletedTask;
}

static Task TestTooltipRendersEscapedMath()
{
    var textBlock = RenderTooltipMarkdown(
        @"公式：\\(a_1 + b^2\\)",
        new MarkdownRenderOptions(true, false, MarkdownColorRenderModes.SemanticTags));
    var rendered = CollectRunText(textBlock);

    if (rendered.Contains(@"\(") || rendered.Contains(@"\\("))
        throw new Exception($"Expected escaped math delimiters to be hidden, got: {rendered}");

    if (!rendered.Contains("a₁ + b²"))
        throw new Exception($"Expected formula to be rendered with scripts, got: {rendered}");

    return Task.CompletedTask;
}

static Task TestTooltipSemanticHighlightFallback()
{
    var textBlock = RenderTooltipMarkdown(
        "循环神经网络（RNN）和卷积神经网络（CNN）是关键模型。",
        new MarkdownRenderOptions(true, true, MarkdownColorRenderModes.SemanticTags));

    var highlightedRuns = 0;
    foreach (var inline in textBlock.Inlines!)
    {
        if (inline is Run { Foreground: not null })
            highlightedRuns++;
    }

    if (highlightedRuns == 0)
        throw new Exception("Expected at least one fallback semantic highlight run.");

    return Task.CompletedTask;
}

static Task TestMarkdownColorRendererFactory()
{
    var disabled = MarkdownColorRendererFactory.Create(false, MarkdownColorRenderModes.SemanticTags);
    if (!string.IsNullOrWhiteSpace(disabled.PromptInstructions))
        throw new Exception("Disabled color renderer should not add model prompt instructions.");

    if (disabled.TryGetStyle("key", out _))
        throw new Exception("Disabled color renderer should not style tags.");

    var enabled = MarkdownColorRendererFactory.Create(true, MarkdownColorRenderModes.SemanticTags);
    if (!enabled.PromptInstructions.Contains("<key>"))
        throw new Exception("Semantic color renderer prompt should describe the key tag.");

    if (!enabled.TryGetStyle("warn", out var warnStyle) || warnStyle.Foreground == null)
        throw new Exception("Semantic color renderer should style warning tags.");

    return Task.CompletedTask;
}

static Task TestMarkdownOutputSanitizer()
{
    var plain = MarkdownOutputSanitizer.RemoveColorTags(
        "请关注 <key>收益率</key> 和 <mark type=\"warn\">风险</mark>。");

    if (plain != "请关注 收益率 和 风险。")
        throw new Exception($"Unexpected sanitized text: {plain}");

    return Task.CompletedTask;
}

static Task TestMarkdownRenderingPrompt()
{
    var config = new AppConfig();
    var prompt = LLMService.BuildMarkdownRenderingPrompt(config);
    if (!prompt.Contains("Math rendering"))
        throw new Exception("Markdown rendering prompt should include math instructions by default.");

    if (prompt.Contains("<key>"))
        throw new Exception("Markdown rendering prompt should not request color tags while disabled.");

    config.EnableMarkdownColorRendering = true;
    prompt = LLMService.BuildMarkdownRenderingPrompt(config);
    if (!prompt.Contains("<key>") || !prompt.Contains("<warn>"))
        throw new Exception("Markdown rendering prompt should request semantic tags when enabled.");

    if (!prompt.Contains("MUST") || !prompt.Contains("at least 2"))
        throw new Exception("Semantic color prompt should require a small number of tags.");

    return Task.CompletedTask;
}

static async Task TestMarkdownRenderingPromptFromBackgroundThread()
{
    var config = new AppConfig
    {
        EnableMarkdownColorRendering = true,
        MarkdownColorRenderMode = MarkdownColorRenderModes.SemanticTags,
    };

    var prompt = await Task.Run(() => LLMService.BuildMarkdownRenderingPrompt(config));
    if (!prompt.Contains("<key>"))
        throw new Exception("Expected semantic color prompt from background thread.");
}

static Task TestLongTextUsesLongerTimeout()
{
    var shortTimeout = LLMService.GetRequestTimeout("hello");
    var longTimeout = LLMService.GetRequestTimeout(new string('中', 2169));

    if (shortTimeout != TimeSpan.FromSeconds(15))
        throw new Exception($"Expected short text timeout to stay 15s, got {shortTimeout.TotalSeconds}s.");

    if (longTimeout < TimeSpan.FromSeconds(60))
        throw new Exception($"Expected long text timeout >= 60s, got {longTimeout.TotalSeconds}s.");

    return Task.CompletedTask;
}

static Task TestTooltipLayoutExpandsForLongText()
{
    var shortLayout = TranslationTooltipWindow.CalculateLayoutMetrics(
        textLength: 120,
        workAreaWidth: 1366,
        workAreaHeight: 768);
    var longLayout = TranslationTooltipWindow.CalculateLayoutMetrics(
        textLength: 1800,
        workAreaWidth: 1366,
        workAreaHeight: 768);

    if (shortLayout.Width != 420 || shortLayout.ContentMaxHeight != 220)
        throw new Exception($"Expected compact short layout, got {shortLayout}.");

    if (longLayout.Width <= shortLayout.Width)
        throw new Exception($"Expected long text width to grow, got {longLayout.Width}.");

    if (longLayout.ContentMaxHeight <= shortLayout.ContentMaxHeight)
        throw new Exception($"Expected long text height to grow, got {longLayout.ContentMaxHeight}.");

    return Task.CompletedTask;
}

static TextBlock RenderTooltipMarkdown(string markdown, MarkdownRenderOptions options)
{
    var method = typeof(TranslationTooltipWindow).GetMethod(
        "RenderMarkdown",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
    if (method == null)
        throw new Exception("RenderMarkdown was not found.");

    var textBlock = new TextBlock
    {
        FontSize = 14,
    };
    method.Invoke(null, new object[] { textBlock, markdown, options });
    return textBlock;
}

static string CollectRunText(TextBlock textBlock)
{
    var text = "";
    foreach (var inline in textBlock.Inlines!)
    {
        if (inline is Run run)
            text += run.Text;
    }

    return text;
}

static bool ContainsHistoryId(IReadOnlyList<TranslationHistoryEntry> entries, string id)
{
    foreach (var entry in entries)
    {
        if (entry.Id == id)
            return true;
    }

    return false;
}

static async Task WithTempConfigHome(Func<Task> body)
{
    var previousConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    var tempConfigHome = Path.Combine(Path.GetTempPath(), "axue-history-test-" + Guid.NewGuid());
    Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", tempConfigHome);

    try
    {
        await body();
    }
    finally
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", previousConfigHome);
        if (Directory.Exists(tempConfigHome))
            Directory.Delete(tempConfigHome, recursive: true);
    }
}

static string GetRepoPath(string relativePath)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "SpeedTranslate.Linux")))
            return Path.Combine(dir.FullName, relativePath);
        dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate repository root.");
}

internal sealed class WaitingHttpMessageHandler : HttpMessageHandler
{
    public TaskCompletionSource RequestStarted { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
    }
}

internal sealed class RespondingHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;

    public string LastRequestBody { get; private set; } = "";

    public RespondingHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestBody = request.Content == null
            ? ""
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody),
        };
    }
}
