using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using SpeedTranslate.Linux.Models;
using SpeedTranslate.Linux.ViewModels;
using SpeedTranslate.Linux.Views;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// 协调全局热键 → 抓取文本 → LLM 翻译 → 粘贴替换的完整流程。
/// </summary>
public sealed class TranslationCoordinator
{
    private readonly MainWindowViewModel _vm;
    private readonly TranslationStatusWindow _statusWindow;
    private TranslationTooltipWindow? _tooltipWindow;
    private readonly LLMService _llm = new();
    private readonly InputSimulator _input = new();
    private readonly ClipboardService _clipboard = new();
    private int _isTranslating;

    public TranslationCoordinator(MainWindowViewModel vm, TranslationStatusWindow statusWindow)
    {
        _vm = vm;
        _statusWindow = statusWindow;
    }

    public void SetTooltipWindow(TranslationTooltipWindow tooltipWindow)
    {
        _tooltipWindow = tooltipWindow;
    }

    public void Trigger()
    {
        _ = Task.Run(RunFlowAsync);
    }

    public void TriggerTooltip()
    {
        _ = Task.Run(RunTooltipFlowAsync);
    }

    private async Task RunTooltipFlowAsync()
    {
        if (Interlocked.Exchange(ref _isTranslating, 1) == 1)
        {
            DebugLog.Write("[Coord] tooltip: already translating, skip");
            return;
        }

        DebugLog.Write("[Coord] tooltip flow start");
        try
        {
            var config = _vm.CurrentConfig;
            if (!config.EnableSelectionMode && !config.EnableAllTextMode)
            {
                DebugLog.Write("[Coord] tooltip: both modes disabled, exit");
                return;
            }

            var (cx, cy) = X11Mouse.GetPosition();
            var cursorPos = new Avalonia.PixelPoint(cx, cy);
            DebugLog.Write($"[Coord] tooltip: cursor at ({cx},{cy})");

            // 显示 loading 状态窗
            await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.ShowAtCursor(cx, cy));

            string sourceText = "";

            // 划词模式：读 PRIMARY selection
            if (config.EnableSelectionMode)
            {
                var primary = await _clipboard.GetPrimarySelectionAsync();
                DebugLog.Write($"[Coord] tooltip: PRIMARY (len={primary.Length}): {Trunc(primary)}");
                if (!string.IsNullOrWhiteSpace(primary))
                    sourceText = primary;
            }

            // 全选模式
            if (string.IsNullOrWhiteSpace(sourceText) && config.EnableAllTextMode)
            {
                var targetWindow = await InputSimulator.GetActiveWindowIdAsync();
                var marker = $"__AXUETRANSLATE_EMPTY_MARKER_{Guid.NewGuid()}__";
                await _clipboard.SetClipboardTextAsync(marker);
                await _input.SendSelectAllAsync(targetWindow);
                await _input.SendCopyAsync(targetWindow);
                var read = await _clipboard.GetClipboardTextAsync();
                DebugLog.Write($"[Coord] tooltip: allText (len={read.Length}): {Trunc(read)}");
                if (!string.IsNullOrEmpty(read) && read != marker)
                    sourceText = read;
            }

            if (string.IsNullOrWhiteSpace(sourceText))
            {
                DebugLog.Write("[Coord] tooltip: no source text, hide and exit");
                await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.HideWithFade());
                return;
            }

            DebugLog.Write($"[Coord] tooltip: calling LLM (lang={config.TargetLanguage})");
            var translated = await _llm.TranslateAsync(sourceText, config);
            DebugLog.Write($"[Coord] tooltip: LLM returned (len={translated.Length}): {Trunc(translated)}");

            // 隐藏 loading 窗，弹出浮窗
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusWindow.HideWithFade();
                if (_tooltipWindow != null)
                {
                    _tooltipWindow.ShowTooltip(
                        sourceText, translated, config, _llm, _input, cursorPos);
                }
            });

            DebugLog.Write("[Coord] tooltip flow OK");
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Coord] tooltip exception: {ex.GetType().Name}: {ex.Message}");
            ConfigManager.WriteErrorLog("TooltipFlow", ex);
            var friendly = MapErrorMessage(ex.Message);
            await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.ShowError(friendly));
        }
        finally
        {
            Interlocked.Exchange(ref _isTranslating, 0);
            DebugLog.Write("[Coord] tooltip flow end");
        }
    }

    private async Task RunFlowAsync()
    {
        if (Interlocked.Exchange(ref _isTranslating, 1) == 1)
        {
            DebugLog.Write("[Coord] already translating, skip");
            return;
        }

        DebugLog.Write("[Coord] flow start");
        try
        {
            var config = _vm.CurrentConfig;
            if (!config.EnableSelectionMode && !config.EnableAllTextMode)
            {
                DebugLog.Write("[Coord] both modes disabled, exit");
                return;
            }

            // 在做任何事之前先记录目标窗口 ID。粘贴/复制时需要把焦点显式切回来，
            // 否则状态悬浮窗会抢走焦点导致 Ctrl+C/V 落到悬浮窗上。
            var targetWindow = await InputSimulator.GetActiveWindowIdAsync();
            DebugLog.Write($"[Coord] target window id: {targetWindow ?? "(unknown)"}");

            // 1. 备份原剪贴板
            var originalClipboard = await _clipboard.GetClipboardTextAsync(retryCount: 2, delayMs: 30);
            DebugLog.Write($"[Coord] backed up clipboard (len={originalClipboard.Length})");

            // 2. 显示翻译中悬浮窗
            var (cx, cy) = X11Mouse.GetPosition();
            DebugLog.Write($"[Coord] cursor at ({cx},{cy})");
            await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.ShowAtCursor(cx, cy));

            string sourceText = "";
            var isAllTextMode = false;

            try
            {
                // 3. 划词模式：优先读 X11 PRIMARY selection（鼠标选中即在 PRIMARY，零触发延迟）
                if (config.EnableSelectionMode)
                {
                    var primary = await _clipboard.GetPrimarySelectionAsync();
                    DebugLog.Write($"[Coord] PRIMARY read (len={primary.Length}): {Trunc(primary)}");
                    if (!string.IsNullOrWhiteSpace(primary))
                    {
                        sourceText = primary;
                    }
                }

                // 4. 全选翻译模式
                if (string.IsNullOrWhiteSpace(sourceText) && config.EnableAllTextMode)
                {
                    isAllTextMode = true;
                    var marker = $"__AXUETRANSLATE_EMPTY_MARKER_{Guid.NewGuid()}__";
                    await _clipboard.SetClipboardTextAsync(marker);
                    DebugLog.Write("[Coord] wrote allText marker");
                    await _input.SendSelectAllAsync(targetWindow);
                    await _input.SendCopyAsync(targetWindow);
                    DebugLog.Write("[Coord] sent Ctrl+A then Ctrl+C");
                    var read = await _clipboard.GetClipboardTextAsync();
                    DebugLog.Write($"[Coord] allText read (len={read.Length}): {Trunc(read)}");
                    if (!string.IsNullOrEmpty(read) && read != marker)
                    {
                        sourceText = read;
                    }
                }

                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    DebugLog.Write("[Coord] no source text, hide and exit");
                    await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.HideWithFade());
                    return;
                }

                DebugLog.Write($"[Coord] calling LLM (model={config.SelectedModel}, lang={config.TargetLanguage})");
                var translated = await _llm.TranslateAsync(sourceText, config);
                DebugLog.Write($"[Coord] LLM returned (len={translated.Length}): {Trunc(translated)}");

                // 6. 写入翻译结果剪贴板
                await _clipboard.SetClipboardTextAsync(translated);
                DebugLog.Write("[Coord] wrote translated text to clipboard");

                // 7. 隐藏悬浮窗后再粘贴，避免抢焦点
                await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.HideWithFade());

                if (isAllTextMode)
                {
                    await _input.SendSelectAllAsync(targetWindow);
                    DebugLog.Write("[Coord] sent Ctrl+A before paste (all-text mode)");
                }
                await _input.SendPasteAsync(targetWindow);
                DebugLog.Write("[Coord] sent Ctrl+V");
                DebugLog.Write("[Coord] flow OK");
            }
            catch (Exception ex)
            {
                DebugLog.Write($"[Coord] inner exception: {ex.GetType().Name}: {ex.Message}");
                ConfigManager.WriteErrorLog("翻译流程异常", ex);
                var friendly = MapErrorMessage(ex.Message);
                await Dispatcher.UIThread.InvokeAsync(() => _statusWindow.ShowError(friendly));
            }
            finally
            {
                // 8. Linux 桌面下保留翻译结果在剪贴板，便于用户进一步使用
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Coord] outer exception: {ex.Message}");
            ConfigManager.WriteErrorLog("Coordinator outer", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _isTranslating, 0);
            DebugLog.Write("[Coord] flow end");
        }
    }

    private static string Trunc(string s) => s.Length > 80 ? s[..80] + "..." : s;

    private static string MapErrorMessage(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "未知错误";
        if (raw.Contains("401")) return "API Key 鉴权失败 (401)";
        if (raw.Contains("403")) return "无访问权限 (403)";
        if (raw.Contains("404")) return "模型名或接口不存在 (404)";
        if (raw.Contains("429")) return "限流或额度不足 (429)";
        if (raw.Contains("500")) return "服务器内部错误 (500)";
        if (raw.Contains("Timeout") || raw.Contains("canceled") || raw.Contains("timed out"))
            return "网络请求超时";
        return raw.Length > 22 ? raw[..20] + ".." : raw;
    }
}
