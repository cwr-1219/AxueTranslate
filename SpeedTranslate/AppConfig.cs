using System;
using System.Windows.Input;

namespace SpeedTranslate
{
    public class AppConfig
    {
        // 模型选择: "DeepSeek", "XiaoMi", "Custom"
        public string SelectedModel { get; set; } = "DeepSeek";

        // DeepSeek 配置
        public string DeepSeekApiKey { get; set; } = "";
        public string DeepSeekModel { get; set; } = "deepseek-chat";
        public string DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";

        // 小米大模型配置
        public string XiaoMiApiKey { get; set; } = "";
        public string XiaoMiModel { get; set; } = "";
        public string XiaoMiUrl { get; set; } = "";

        // 自定义模型配置
        public string CustomApiKey { get; set; } = "";
        public string CustomModel { get; set; } = "";
        public string CustomUrl { get; set; } = "";

        // 目标语种，例如："English", "Chinese", "Japanese", "Korean", "Auto"
        public string TargetLanguage { get; set; } = "Auto";

        // 翻译风格 (针对英语进行口语等风格优化)
        // "Standard" (标准), "AmericanColloquial" (美式口语), "BritishColloquial" (英式口语), "Business" (商务职场), "Academic" (学术雅思), "Concise" (极简流利)
        public string TranslationStyle { get; set; } = "Standard";

        // 快捷键反应模式
        public bool EnableSelectionMode { get; set; } = true;     // 划词/选中文本翻译
        public bool EnableAllTextMode { get; set; } = true;       // 未选中时翻译全部内容

        // 快捷键配置
        // 默认快捷键：Ctrl + Alt + T
        public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
        public Key HotkeyKey { get; set; } = Key.T;
        public string HotkeyText { get; set; } = "Ctrl + Alt + T";
    }
}
