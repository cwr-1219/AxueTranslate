namespace SpeedTranslate.Linux.Models;

public class AppConfig
{
    public string SelectedModel { get; set; } = "DeepSeek";

    public string DeepSeekApiKey { get; set; } = "";
    public string DeepSeekModel { get; set; } = "deepseek-chat";
    public string DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";

    public string XiaoMiApiKey { get; set; } = "";
    public string XiaoMiModel { get; set; } = "";
    public string XiaoMiUrl { get; set; } = "";

    public string CustomApiKey { get; set; } = "";
    public string CustomModel { get; set; } = "";
    public string CustomUrl { get; set; } = "";

    public string TargetLanguage { get; set; } = "Auto";

    public string TranslationStyle { get; set; } = "Standard";

    public bool EnableSelectionMode { get; set; } = true;
    public bool EnableAllTextMode { get; set; } = true;

    public HotkeyDescriptor Hotkey { get; set; } = new();
    public HotkeyDescriptor TooltipHotkey { get; set; } = new()
    {
        Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt,
        Key = "F",
    };
}
