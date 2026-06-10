using System;
using System.Threading;
using System.Threading.Tasks;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.Services;
using SpeedTranslate.Linux.Views;

var tests = new (string Name, Func<Task> Run)[]
{
    ("LLMService.TranslateAsync honors an already-canceled token", TestTranslateAsyncHonorsCanceledToken),
    ("TranslationCoordinator exposes a shutdown cancellation hook", TestCoordinatorExposesCancelPendingWork),
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

    if (shortLayout.Width != 350 || shortLayout.ContentMaxHeight != 220)
        throw new Exception($"Expected compact short layout, got {shortLayout}.");

    if (longLayout.Width <= shortLayout.Width)
        throw new Exception($"Expected long text width to grow, got {longLayout.Width}.");

    if (longLayout.ContentMaxHeight <= shortLayout.ContentMaxHeight)
        throw new Exception($"Expected long text height to grow, got {longLayout.ContentMaxHeight}.");

    return Task.CompletedTask;
}
