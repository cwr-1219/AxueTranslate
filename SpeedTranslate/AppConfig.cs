using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace SpeedTranslate
{
    public class ModelConfig
    {
        public string DisplayName { get; set; } = "";
        public string ApiUrl { get; set; } = "";
        public string ApiKey { get; set; } = "";
        public string ModelName { get; set; } = "";
    }

    public class AppConfig
    {
        // 当前选中的模型，关联到 ModelConfigs 列表中的 DisplayName
        public string SelectedModel { get; set; } = "DeepSeek 大模型";

        // 动态的模型配置列表
        public List<ModelConfig> ModelConfigs { get; set; } = new List<ModelConfig>();

        // 向下兼容历史版本的字段 (用于读取旧 config.json 时的备用迁移)
        public string DeepSeekApiKey { get; set; } = "";
        public string DeepSeekModel { get; set; } = "deepseek-chat";
        public string DeepSeekUrl { get; set; } = "https://api.deepseek.com/v1";

        public string XiaoMiApiKey { get; set; } = "";
        public string XiaoMiModel { get; set; } = "";
        public string XiaoMiUrl { get; set; } = "";

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
        // 默认快捷键：Ctrl + Alt + T (翻译替换)
        public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
        public Key HotkeyKey { get; set; } = Key.T;
        public string HotkeyText { get; set; } = "Ctrl + Alt + T";

        // 划词弹窗模式配置 (仅弹窗，不替换)
        public bool EnableTooltipMode { get; set; } = true;
        // 默认快捷键：Ctrl + Alt + F
        public ModifierKeys TooltipHotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Alt;
        public Key TooltipHotkeyKey { get; set; } = Key.F;
        public string TooltipHotkeyText { get; set; } = "Ctrl + Alt + F";

        // 界面显示语言："zh-CN" (简体中文), "en-US" (English)
        public string AppLanguage { get; set; } = "zh-CN";
    }
}
