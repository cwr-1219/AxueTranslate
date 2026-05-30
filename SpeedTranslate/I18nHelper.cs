using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpeedTranslate
{
    public static class I18nHelper
    {
        public static string CurrentLanguage { get; set; } = "zh-CN";

        private static readonly Dictionary<string, string> EnTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Title & Headers
            { "阿雪翻译助手", "AxueTranslate" },
            { "大模型设置", "LLM API Settings" },
            { "快捷键激活", "Hotkey Activation" },
            { "划词翻译悬浮窗", "Floating Tooltip Window" },
            { "划词翻译", "Selection Translation" },
            { "系统提示词", "System Prompt Settings" },
            { "系统提示词 (Prompt)", "System Prompt" },
            
            // Labels
            { "API 接口地址", "API Base URL" },
            { "密钥 (API Key)", "API Key" },
            { "模型名称", "Model Name" },
            { "目标语种", "Target Language" },
            { "翻译风格", "Translation Style" },
            
            // Hotkeys
            { "翻译并替换快捷键", "Translate & Replace Hotkey" },
            { "划词翻译悬浮窗快捷键", "Tooltip Floating Window Hotkey" },
            { "选中文本快捷键翻译", "Translate selected text" },
            { "未选中时翻译框内全部文字", "Translate all text when no text selected" },
            { "启用划词翻译悬浮窗", "Enable Floating Tooltip on Selection" },
            
            // Buttons & Actions
            { "保存并应用", "Save & Apply" },
            { "测试连接", "Test Connection" },
            { "获取模型", "Fetch Models" },
            { "点击输入新快捷键", "Click to record new hotkey" },
            { "保存设置", "Save Settings" },
            { "语言切换", "Language" },
            
            // Placeholders / Hints
            { "例如：https://api.deepseek.com/v1", "e.g. https://api.deepseek.com/v1" },
            { "输入您的 API 密钥", "Enter your API Key" },
            { "选填，不填则使用系统默认翻译提示词", "Optional. Leave blank to use system default prompt." },

            // Messages / Dialogs / System Trays
            { "提示", "Tips" },
            { "配置已保存", "Settings saved successfully" },
            { "请先填写 API 接口地址和 API Key！", "Please fill in API Base URL and API Key first!" },
            { "接口未返回任何可用模型。", "The endpoint did not return any models." },
            { "获取模型成功！已同步至下拉列表。", "Models fetched successfully! Synced to dropdown." },
            { "退出", "Exit" },
            { "显示主面板", "Show Settings" },
            { "双击图标打开设置", "Double click to open settings" },
            { "正在翻译中...", "Translating..." },
            { "翻译失败，请检查配置或网络", "Translation failed, please check network or configs" },
            { "接口返回了网页而非 JSON 数据，请检查接口地址是否需要补全 /v1 后缀或端口是否正确。", "HTML returned instead of JSON. Please check if you need to add /v1 to the URL." },
            { "模型未返回任何结果。", "Model returned empty response." },
            { "模型响应内容为空。", "Model response content is empty." },
            { "无法连接到 API 服务:", "Unable to connect to API service:" },
            { "API 请求失败 (HTTP", "API Request Failed (HTTP" },
            { "热键冲突", "Hotkey Conflict" },
            { "系统内置配置，无法删除。", "Built-in configurations cannot be deleted." },
            { "确认删除", "Confirm Delete" },
            { "配置名称不能为空！", "Configuration name cannot be empty!" },
            { "名称已存在", "Name Exists" },
            
            // Combobox items (Target languages)
            { "Auto (自动检测并中英互译)", "Auto (Translate Chinese/English)" },
            { "Chinese (简体中文)", "Chinese (简体中文)" },
            { "English (英文)", "English" },
            { "Japanese (日语)", "Japanese" },
            { "Korean (韩语)", "Korean" },
            { "French (法语)", "French" },
            { "German (德语)", "German" },
            { "Spanish (西班牙语)", "Spanish" },
            
            // Style Combobox
            { "Standard (标准风格)", "Standard" },
            { "AmericanColloquial (美式口语)", "American Casual" },
            { "BritishColloquial (英式口语)", "British Casual" },
            { "Business (商务职场)", "Business Formal" },
            { "Academic (学术雅思)", "Academic Formal" },
            { "Concise (极简流利)", "Concise & Fluent" },
            { "全局替换快捷键 [{0}] 注册失败！\n该热键可能已被其他程序占用，请重新录入并保存。", "Global replace hotkey [{0}] registration failed!\nThe hotkey may be occupied by another program. Please record and save a different hotkey." },
            { "全局弹窗快捷键 [{0}] 注册失败！\n该热键可能已被其他程序占用，请重新录入并保存。", "Global tooltip hotkey [{0}] registration failed!\nThe hotkey may be occupied by another program. Please record and save a different hotkey." },
            { "确定要删除 API 配置 [{0}] 吗？", "Are you sure you want to delete API configuration [{0}]?" },
            { "已存在名为 [{0}] 的配置，请换个名称。", "Configuration [{0}] already exists. Please choose a different name." },
            { "已存在名称为 [{0}] 的大模型配置，请使用其他名称。", "Configuration [{0}] already exists. Please use a different name." },
        };

        private static readonly Dictionary<string, string> ZhTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        static I18nHelper()
        {
            // 初始化中文词典，直接将键映射给自己
            foreach (var key in EnTranslations.Keys)
            {
                ZhTranslations[key] = key;
            }
            // 建立反向映射字典，以便英文切换回中文
            foreach (var kvp in EnTranslations)
            {
                ZhTranslations[kvp.Value] = kvp.Key;
            }
        }

        public static string Get(string key, params object[] args)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            string trimmedKey = key.Trim();
            string val = trimmedKey;
            if (CurrentLanguage == "en-US")
            {
                if (EnTranslations.TryGetValue(trimmedKey, out var translation))
                {
                    val = translation;
                }
            }
            else if (CurrentLanguage == "zh-CN")
            {
                if (ZhTranslations.TryGetValue(trimmedKey, out var translation))
                {
                    val = translation;
                }
            }

            if (args != null && args.Length > 0)
            {
                try
                {
                    val = string.Format(val, args);
                }
                catch { }
            }
            return val;
        }

        public static void ApplyLanguage(DependencyObject parent)
        {
            if (parent == null) return;
            if (!(parent is Visual || parent is System.Windows.Media.Media3D.Visual3D)) return;

            // 递归遍历 Visual Tree 进行文字替换
            int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childrenCount; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                switch (child)
                {
                    case TextBlock textBlock:
                        if (!string.IsNullOrEmpty(textBlock.Text))
                        {
                            textBlock.Text = Get(textBlock.Text);
                        }
                        break;
                    case System.Windows.Controls.Label label:
                        if (label.Content is string contentStr && !string.IsNullOrEmpty(contentStr))
                        {
                            label.Content = Get(contentStr);
                        }
                        break;
                    case System.Windows.Controls.Button button:
                        if (button.Content is string btnStr && !string.IsNullOrEmpty(btnStr))
                        {
                            button.Content = Get(btnStr);
                        }
                        break;
                    case System.Windows.Controls.CheckBox checkBox:
                        if (checkBox.Content is string cbStr && !string.IsNullOrEmpty(cbStr))
                        {
                            checkBox.Content = Get(cbStr);
                        }
                        break;
                    case MenuItem menuItem:
                        if (menuItem.Header is string menuStr && !string.IsNullOrEmpty(menuStr))
                        {
                            menuItem.Header = Get(menuStr);
                        }
                        break;
                    case System.Windows.Controls.GroupBox groupBox:
                        if (groupBox.Header is string gbStr && !string.IsNullOrEmpty(gbStr))
                        {
                            groupBox.Header = Get(gbStr);
                        }
                        break;
                    case ComboBoxItem comboBoxItem:
                        if (comboBoxItem.Content is string cbiStr && !string.IsNullOrEmpty(cbiStr))
                        {
                            comboBoxItem.Content = Get(cbiStr);
                        }
                        break;
                }

                // 递归子元素
                ApplyLanguage(child);
            }
        }
    }
}
