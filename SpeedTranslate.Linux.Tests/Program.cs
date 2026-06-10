using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
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
    ("TranslationCoordinator auto summary policy uses the configured threshold", TestAutoSummaryPolicy),
    ("TranslationTooltipWindow parses simple Markdown lines", TestMarkdownParsing),
    ("TranslationTooltipWindow controls have clipping-safe dimensions", TestTooltipControlDimensions),
    ("MarkdownMathRenderer converts common LaTeX syntax", TestMarkdownMathRendering),
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
