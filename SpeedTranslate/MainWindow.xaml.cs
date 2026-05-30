using System;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Application = System.Windows.Application;
using Clipboard = System.Windows.Clipboard;
using IDataObject = System.Windows.IDataObject;
using MessageBox = System.Windows.MessageBox;

namespace SpeedTranslate
{
    public partial class MainWindow : Window
    {
        private const int HOTKEY_ID = 9000;
        private AppConfig _config = new AppConfig();
        private LLMService _llmService = new LLMService();
        private TrayIconManager? _trayIconManager;
        private TranslationStatusWindow? _statusWindow;

        // 临时存储用户录入的快捷键
        private ModifierKeys _currentModifiers;
        private Key _currentKey;
        private string _currentHotkeyText = "";

        // 标记是否正在执行翻译，防止重复触发
        private bool _isTranslating = false;

        // 标记是否真的退出程序，若是关闭窗口则只隐藏到托盘
        private bool _isRealExit = false;

        // 保存上一次选中的模型，用于在切换模型时暂存输入框内容
        private string _lastSelectedModel = "DeepSeek";

        public MainWindow()
        {
            InitializeComponent();
            
            // 绑定 Loaded 事件
            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 初始化托盘图标
            _trayIconManager = new TrayIconManager(this);
            _trayIconManager.Initialize();

            // 2. 初始化翻译状态悬浮窗
            _statusWindow = new TranslationStatusWindow();

            // 3. 加载配置文件
            _config = ConfigManager.LoadConfig();

            // 初始化默认大模型列表 (老用户自动迁移)
            InitializeDefaultModelConfigs();

            // 4. 回填 UI
            _currentModifiers = _config.HotkeyModifiers;
            _currentKey = _config.HotkeyKey;
            _currentHotkeyText = _config.HotkeyText;
            HotkeyTextBox.Text = _currentHotkeyText;

            // 设置目标语种选中项
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.TargetLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            // 设置英语说话风格选中项
            foreach (ComboBoxItem item in StyleComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.TranslationStyle)
                {
                    StyleComboBox.SelectedItem = item;
                    break;
                }
            }

            // 设置触发开关状态
            SelectionModeCheckBox.IsChecked = _config.EnableSelectionMode;
            AllTextModeCheckBox.IsChecked = _config.EnableAllTextMode;

            // 根据初始语种控制英语风格面板显隐
            StyleSettingsPanel.Visibility = _config.TargetLanguage == "English" ? Visibility.Visible : Visibility.Collapsed;

            // 设置模型选中项，并初始化输入框 (解绑事件以防启动初始化时因触发事件而覆盖内存配置)
            ModelSelectComboBox.SelectionChanged -= ModelSelectComboBox_SelectionChanged;
            
            // 刷新 ComboBox
            RefreshModelSelectComboBox();

            _lastSelectedModel = _config.SelectedModel;
            bool foundSelected = false;
            for (int i = 0; i < ModelSelectComboBox.Items.Count; i++)
            {
                if (ModelSelectComboBox.Items[i] is ComboBoxItem item && item.Content?.ToString() == _config.SelectedModel)
                {
                    ModelSelectComboBox.SelectedIndex = i;
                    foundSelected = true;
                    break;
                }
            }
            if (!foundSelected && ModelSelectComboBox.Items.Count > 0)
            {
                ModelSelectComboBox.SelectedIndex = 0;
                _config.SelectedModel = (ModelSelectComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                _lastSelectedModel = _config.SelectedModel;
            }

            LoadModelConfigToUI(_config.SelectedModel);
            ModelSelectComboBox.SelectionChanged += ModelSelectComboBox_SelectionChanged;

            // 5. 注册全局热键
            RegisterGlobalHotkey();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 添加 Win32 消息钩子来接收热键消息
            var helper = new WindowInteropHelper(this);
            HwndSource? source = HwndSource.FromHwnd(helper.Handle);
            source?.AddHook(HwndHook);
        }

        /// <summary>
        /// 接收 Win32 消息循环
        /// </summary>
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == HotkeyHelper.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // 触发快捷键，异步执行翻译替换
                TriggerTranslationFlow();
                handled = true;
            }
            return IntPtr.Zero;
        }

        /// <summary>
        /// 注册全局热键
        /// </summary>
        private void RegisterGlobalHotkey()
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero) return;

            bool success = HotkeyHelper.Register(helper.Handle, HOTKEY_ID, _currentModifiers, _currentKey);
            if (!success)
            {
                MessageBox.Show($"全局快捷键 [{_currentHotkeyText}] 注册失败！\n该热键可能已被其他程序占用，请重新录入并保存。", "热键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 快捷键录入拦截
        /// </summary>
        private void HotkeyTextBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true; // 阻止默认的文本输入

            // 获取当前按下的键 (处理 SystemKey 如 Alt)
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            // 忽略单独按下的修饰键本身
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // 获取修饰键
            ModifierKeys modifiers = Keyboard.Modifiers;

            // 必须带修饰键，或者如果是 F1-F24 功能键允许单按
            if (modifiers == ModifierKeys.None && !(key >= Key.F1 && key <= Key.F24))
            {
                return;
            }

            // 更新暂存的按键值
            _currentModifiers = modifiers;
            _currentKey = key;

            // 拼接可读快捷键文本
            StringBuilder sb = new StringBuilder();
            if ((modifiers & ModifierKeys.Control) != 0) sb.Append("Ctrl + ");
            if ((modifiers & ModifierKeys.Alt) != 0) sb.Append("Alt + ");
            if ((modifiers & ModifierKeys.Shift) != 0) sb.Append("Shift + ");
            if ((modifiers & ModifierKeys.Windows) != 0) sb.Append("Win + ");
            sb.Append(key.ToString());

            _currentHotkeyText = sb.ToString();
            HotkeyTextBox.Text = _currentHotkeyText;
        }

        /// <summary>
        /// 目标语种下拉框变化事件：用于智能展示/隐藏英文说话风格面板
        /// </summary>
        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StyleSettingsPanel == null || LanguageComboBox == null) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string tag = selectedItem.Tag?.ToString() ?? "";
                // 如果目标是英语，就显示风格配置项，否则隐藏
                StyleSettingsPanel.Visibility = tag == "English" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 模型下拉框切换事件：实现多套配置切换暂存
        /// </summary>
        private void ModelSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ApiUrlTextBox == null || ApiKeyTextBox == null || ModelNameTextBox == null) return;

            // 确保 SelectedItem 不为 null，且为 ComboBoxItem 类型
            if (ModelSelectComboBox.SelectedItem is not ComboBoxItem selectedItem || selectedItem.Content == null) return;

            // 1. 先把当前 UI 上的文本暂存回上一个选中的模型配置字段
            SaveModelConfigFromUI(_lastSelectedModel);

            // 2. 更新当前选中模型
            string selectedModel = selectedItem.Content.ToString();
            _lastSelectedModel = selectedModel;

            // 3. 将新选中的模型配置加载到 UI 输入框
            LoadModelConfigToUI(selectedModel);

            // 控制删除按钮是否可用：内置模型不允许删除
            if (DeleteConfigButton != null)
            {
                bool isBuiltIn = selectedModel == "DeepSeek 大模型" || 
                                 selectedModel == "小米大模型" || 
                                 selectedModel == "OpenAI (ChatGPT)" || 
                                 selectedModel == "Anthropic (Claude)" || 
                                 selectedModel == "Google (Gemini)" || 
                                 selectedModel == "自定义模型";
                DeleteConfigButton.IsEnabled = !isBuiltIn;
                DeleteConfigButton.Opacity = isBuiltIn ? 0.4 : 1.0;
            }
        }

        /// <summary>
        /// 从 UI 抓取配置保存到内存实体中
        /// </summary>
        private void SaveModelConfigFromUI(string modelDisplayName)
        {
            if (string.IsNullOrWhiteSpace(modelDisplayName)) return;
            var modelConfig = _config.ModelConfigs.Find(m => m.DisplayName == modelDisplayName);
            if (modelConfig != null)
            {
                modelConfig.ApiUrl = ApiUrlTextBox.Text.Trim();
                modelConfig.ApiKey = ApiKeyTextBox.Text.Trim();
                modelConfig.ModelName = ModelNameTextBox.Text.Trim();
            }
        }

        /// <summary>
        /// 将指定模型的配置加载到 UI
        /// </summary>
        private void LoadModelConfigToUI(string modelDisplayName)
        {
            if (string.IsNullOrWhiteSpace(modelDisplayName)) return;
            var modelConfig = _config.ModelConfigs.Find(m => m.DisplayName == modelDisplayName);
            if (modelConfig != null)
            {
                ApiUrlTextBox.Text = modelConfig.ApiUrl;
                ApiKeyTextBox.Text = modelConfig.ApiKey;
                ModelNameTextBox.Text = modelConfig.ModelName;
            }
        }

        /// <summary>
        /// 点击保存按钮：汇总所有配置并写入本地文件，重新注册热键
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 保存当前选中的模型及其配置
            if (ModelSelectComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Content != null)
            {
                string selectedModelName = selectedItem.Content.ToString();
                _config.SelectedModel = selectedModelName;
                SaveModelConfigFromUI(selectedModelName);
            }

            // 保存选中的目标语种
            if (LanguageComboBox.SelectedItem is ComboBoxItem langItem)
            {
                _config.TargetLanguage = langItem.Tag?.ToString() ?? "Auto";
            }

            // 保存选中的英文翻译风格
            if (StyleComboBox.SelectedItem is ComboBoxItem styleItem)
            {
                _config.TranslationStyle = styleItem.Tag?.ToString() ?? "Standard";
            }

            // 保存触发开关状态
            _config.EnableSelectionMode = SelectionModeCheckBox.IsChecked ?? true;
            _config.EnableAllTextMode = AllTextModeCheckBox.IsChecked ?? true;

            // 保存快捷键
            _config.HotkeyModifiers = _currentModifiers;
            _config.HotkeyKey = _currentKey;
            _config.HotkeyText = _currentHotkeyText;

            // 写入本地 config.json
            ConfigManager.SaveConfig(_config);

            // 重新注册热键
            RegisterGlobalHotkey();

            // 隐藏窗口到后台运行
            this.Hide();
        }

        /// <summary>
        /// 拦截窗口关闭事件，防止真正退出，而是最小化到托盘
        /// </summary>
        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        /// <summary>
        /// 核心逻辑：触发复制 -> 提取文本 -> 翻译 -> 粘贴替换
        /// </summary>
        private async void TriggerTranslationFlow()
        {
            if (_isTranslating) return;
            _isTranslating = true;

            // 记录当前拥有焦点的原活动窗口，以便在翻译完毕粘贴前恢复它
            IntPtr targetWindow = IntPtr.Zero;
            try
            {
                targetWindow = InputHelper.GetForegroundWindow();
            }
            catch { }

            bool isAllTextModeTriggered = false;

            // 0. 如果用户把两个翻译模式都关了，直接退出
            if (!_config.EnableSelectionMode && !_config.EnableAllTextMode)
            {
                _isTranslating = false;
                return;
            }

            // 1. 备份原剪贴板内容
            IDataObject? originalClipboardData = null;
            try
            {
                originalClipboardData = Clipboard.GetDataObject();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"备份剪贴板失败: {ex.Message}");
            }

            try
            {
                // 生成一个独特的空值检测标记，避免使用 Clipboard.Clear() 容易受第三方剪贴板工具干扰或被系统自动恢复
                string marker = $"__AXUETRANSLATE_EMPTY_MARKER_{Guid.NewGuid()}__";
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SetClipboardTextWithRetry(marker);
                });

                // 2. 显示“翻译中”悬浮框并自动定位在光标处
                _statusWindow?.Dispatcher.Invoke(() => _statusWindow.ShowAtCursor());

                string sourceText = "";

                // 3. 模式一：划词选中文本翻译
                if (_config.EnableSelectionMode)
                {
                    // 模拟 Ctrl+C 复制
                    await InputHelper.SimulateCopyAsync();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        string temp = GetClipboardTextWithRetry();
                        // 只有读取到的内容不为空，且与我们的特定标记不同，才代表用户确实划词复制到了新内容
                        if (!string.IsNullOrEmpty(temp) && temp != marker)
                        {
                            sourceText = temp;
                        }
                    });
                }

                // 4. 模式二：自动翻译全部内容 (如果没有获取到划词，且开启了全选翻译)
                if (string.IsNullOrWhiteSpace(sourceText) && _config.EnableAllTextMode)
                {
                    isAllTextModeTriggered = true;
                    string allTextMarker = $"__AXUETRANSLATE_EMPTY_MARKER_{Guid.NewGuid()}__";
                    
                    // 全选复制前再次写入新的临时标记
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        SetClipboardTextWithRetry(allTextMarker);
                    });

                    // 模拟 Ctrl+A 选中全部文本
                    await InputHelper.SimulateSelectAllAsync();
                    // 模拟 Ctrl+C 复制
                    await InputHelper.SimulateCopyAsync();
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        string temp = GetClipboardTextWithRetry();
                        if (!string.IsNullOrEmpty(temp) && temp != allTextMarker)
                        {
                            sourceText = temp;
                        }
                    });
                }

                if (string.IsNullOrWhiteSpace(sourceText))
                {
                    // 依然没获取到任何文本，直接关闭悬浮窗退出
                    _statusWindow?.Dispatcher.Invoke(() => _statusWindow.HideWithFade());
                    _isTranslating = false;
                    return;
                }

                // 5. 异步调用 API 大模型进行翻译
                string translatedText = await _llmService.TranslateAsync(sourceText, _config);

                // 6. 将翻译好的内容写入剪贴板，并模拟粘贴替换
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SetClipboardTextWithRetry(translatedText);
                });

                if (isAllTextModeTriggered)
                {
                    // 在全选翻译模式下，粘贴前再次强行恢复焦点并执行全选，确保 100% 替换整个输入框
                    if (targetWindow != IntPtr.Zero)
                    {
                        try { InputHelper.SetForegroundWindow(targetWindow); } catch { }
                        await Task.Delay(80);
                    }
                    await InputHelper.SimulateSelectAllAsync();
                }

                await InputHelper.SimulatePasteAsync(targetWindow);

                // 7. 翻译顺利完成，关闭悬浮窗
                _statusWindow?.Dispatcher.Invoke(() => _statusWindow.HideWithFade());
            }
            catch (Exception ex)
            {
                // 8. 详细记录错误到本地 error.log 供排查
                WriteErrorLog("翻译流程执行异常", ex);

                // 根据具体异常特征进行针对性地友好提示，避免一律显示“调用失败”
                string friendlyError = "翻译失败了";
                string exMsg = ex.Message;

                if (ex is ArgumentException)
                {
                    friendlyError = "请先配置 API Key";
                }
                // 捕获可能的多进程剪贴板碰撞异常并做单独友好提示，排除其 HRESULT 错误码 0x800401D3 中包含 "401" 导致的误判
                else if (exMsg.Contains("CLIPBRD_E_BAD_DATA") || exMsg.Contains("剪贴板") || exMsg.Contains("0x800401D3"))
                {
                    friendlyError = "剪贴板繁忙，请重试";
                }
                else if (exMsg.Contains("Unauthorized") || (exMsg.Contains("401") && !exMsg.Contains("800401")))
                {
                    friendlyError = "API Key 无效 (401)";
                }
                else if (exMsg.Contains("404"))
                {
                    friendlyError = "模型名或接口不存在(404)";
                }
                else if (exMsg.Contains("429"))
                {
                    friendlyError = "限流或额度不足 (429)";
                }
                else if (exMsg.Contains("500"))
                {
                    friendlyError = "服务器内部错误 (500)";
                }
                else if (exMsg.Contains("Timeout") || exMsg.Contains("canceled") || exMsg.Contains("时间已到"))
                {
                    friendlyError = "网络请求超时";
                }
                else
                {
                    // 自动截短错误信息避免悬浮窗排版破损
                    friendlyError = exMsg.Length > 18 ? exMsg.Substring(0, 16) + ".." : exMsg;
                }

                _statusWindow?.ShowError(friendlyError);
            }
            finally
            {
                // 9. 延迟1秒后恢复用户原始剪贴板内容，避免覆盖复制链
                await Task.Delay(1000);
                if (originalClipboardData != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            Clipboard.SetDataObject(originalClipboardData, true);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"还原剪贴板失败: {ex.Message}");
                        }
                    });
                }
                _isTranslating = false;
            }
        }

        /// <summary>
        /// 带重试的剪贴板读取，防止多进程竞争导致的 0x800401D3 异常
        /// </summary>
        private string GetClipboardTextWithRetry(int retryCount = 5, int delayMs = 80)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text))
                        {
                            return text;
                        }
                    }
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取剪贴板 COM 异常 (重试 {i+1}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"读取剪贴板异常 (重试 {i+1}): {ex.Message}");
                }

                if (i < retryCount - 1)
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
            return string.Empty;
        }

        /// <summary>
        /// 带重试的剪贴板写入，防止多进程竞争导致写入失败
        /// </summary>
        private bool SetClipboardTextWithRetry(string text, int retryCount = 5, int delayMs = 80)
        {
            for (int i = 0; i < retryCount; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"写入剪贴板 COM 异常 (重试 {i+1}): {ex.Message}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"写入剪贴板异常 (重试 {i+1}): {ex.Message}");
                }

                if (i < retryCount - 1)
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
            return false;
        }

        /// <summary>
        /// 当程序退出时，释放资源并销毁托盘
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _isRealExit = true;
            _trayIconManager?.Dispose();
            _statusWindow?.Close();
            base.OnClosed(e);
        }

        /// <summary>
        /// 将详细的异常和调试信息追加写入到本地的 error.log 文件
        /// </summary>
        private void WriteErrorLog(string context, Exception ex)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log");
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}\r\n" +
                                     $"异常消息: {ex.Message}\r\n" +
                                     $"详细堆栈:\r\n{ex.StackTrace}\r\n" +
                                     $"--------------------------------------------------\r\n\r\n";
                System.IO.File.AppendAllText(logPath, logContent);
            }
            catch { }
        }

        private static readonly System.Collections.Generic.List<string> RecommendedModels = new System.Collections.Generic.List<string>
        {
            "deepseek-chat",
            "deepseek-reasoner",
            "gpt-4o",
            "gpt-4o-mini",
            "gpt-4-turbo",
            "claude-3-5-sonnet",
            "claude-3-opus",
            "gemini-1.5-pro",
            "gemini-1.5-flash",
            "mimo-v2.5"
        };

        /// <summary>
        /// 异步获取可用模型列表，并在按钮下方弹出列表以供点选
        /// </summary>
        private async void FetchModelsButton_Click(object sender, RoutedEventArgs e)
        {
            string url = ApiUrlTextBox.Text.Trim();
            string key = ApiKeyTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
            {
                MessageBox.Show("请先填写 API 接口地址和 API Key！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            FetchModelsButton.IsEnabled = false;
            FetchModelsButton.Content = "获取中...";

            try
            {
                var models = await _llmService.GetAvailableModelsAsync(url, key);

                if (models == null || models.Count == 0)
                {
                    MessageBox.Show("接口未返回任何可用模型。", "获取模型", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 创建并弹出 ContextMenu，并赋予已消去白色栏的纯暗黑精致卡片样式
                var contextMenu = new ContextMenu
                {
                    Style = (Style)FindResource("ModernContextMenuStyle")
                };

                foreach (var model in models)
                {
                    var item = new MenuItem
                    {
                        Header = model,
                        Style = (Style)FindResource("ModernMenuItemStyle")
                    };
                    
                    item.Click += (s, ev) =>
                    {
                        ModelNameTextBox.Text = model;
                    };
                    contextMenu.Items.Add(item);
                }

                // 弹出在按钮下方
                contextMenu.PlacementTarget = FetchModelsButton;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
            catch (Exception ex)
            {
                WriteErrorLog("获取可用模型列表失败，将显示常用推荐模型", ex);
                
                // 弹出 ContextMenu 并展示默认推荐模型，彻底解决中转站反向代理不支持 /models 的情况
                var contextMenu = new ContextMenu
                {
                    Style = (Style)FindResource("ModernContextMenuStyle")
                };

                // 增加一个不可点击的提示项
                var tipItem = new MenuItem
                {
                    Header = "⚠️ 获取失败，显示常用大模型：",
                    IsEnabled = false,
                    Style = (Style)FindResource("ModernMenuItemStyle"),
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184))
                };
                contextMenu.Items.Add(tipItem);

                foreach (var model in RecommendedModels)
                {
                    var item = new MenuItem
                    {
                        Header = model,
                        Style = (Style)FindResource("ModernMenuItemStyle")
                    };
                    
                    item.Click += (s, ev) =>
                    {
                        ModelNameTextBox.Text = model;
                    };
                    contextMenu.Items.Add(item);
                }

                // 弹出在按钮下方
                contextMenu.PlacementTarget = FetchModelsButton;
                contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                contextMenu.IsOpen = true;
            }
            finally
            {
                FetchModelsButton.IsEnabled = true;
                FetchModelsButton.Content = "切换模型";
            }
        }

        // ==================== 动态 API 模型配置管理相关 ====================

        private void InitializeDefaultModelConfigs()
        {
            if (_config.ModelConfigs == null)
            {
                _config.ModelConfigs = new List<ModelConfig>();
            }

            if (_config.ModelConfigs.Count == 0)
            {
                // 1. DeepSeek 大模型
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "DeepSeek 大模型",
                    ApiUrl = string.IsNullOrWhiteSpace(_config.DeepSeekUrl) ? "https://api.deepseek.com/v1" : _config.DeepSeekUrl,
                    ApiKey = _config.DeepSeekApiKey,
                    ModelName = string.IsNullOrWhiteSpace(_config.DeepSeekModel) ? "deepseek-chat" : _config.DeepSeekModel
                });

                // 2. 小米大模型
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "小米大模型",
                    ApiUrl = string.IsNullOrWhiteSpace(_config.XiaoMiUrl) ? "https://token-plan-cn.xiaomimimo.com/v1" : _config.XiaoMiUrl,
                    ApiKey = _config.XiaoMiApiKey,
                    ModelName = string.IsNullOrWhiteSpace(_config.XiaoMiModel) ? "mimo-v2.5" : _config.XiaoMiModel
                });

                // 3. OpenAI (ChatGPT)
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "OpenAI (ChatGPT)",
                    ApiUrl = "https://api.openai.com/v1",
                    ApiKey = "",
                    ModelName = "gpt-4o"
                });

                // 4. Anthropic (Claude)
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "Anthropic (Claude)",
                    ApiUrl = "https://api.anthropic.com/v1",
                    ApiKey = "",
                    ModelName = "claude-3-5-sonnet"
                });

                // 5. Google (Gemini)
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "Google (Gemini)",
                    ApiUrl = "https://generativetoolkit.googleapis.com/v1beta/openai",
                    ApiKey = "",
                    ModelName = "gemini-1.5-pro"
                });

                // 6. 自定义模型
                _config.ModelConfigs.Add(new ModelConfig
                {
                    DisplayName = "自定义模型",
                    ApiUrl = _config.CustomUrl,
                    ApiKey = _config.CustomApiKey,
                    ModelName = _config.CustomModel
                });
            }
        }

        private void RefreshModelSelectComboBox()
        {
            ModelSelectComboBox.Items.Clear();
            foreach (var cfg in _config.ModelConfigs)
            {
                ModelSelectComboBox.Items.Add(new ComboBoxItem { Content = cfg.DisplayName });
            }
        }

        /// <summary>
        /// 点击新增模型配置按钮：弹出暗色遮罩模态框
        /// </summary>
        private void AddConfigButton_Click(object sender, RoutedEventArgs e)
        {
            NewConfigNameTextBox.Text = "";
            ModalOverlay.Visibility = Visibility.Visible;
            NewConfigNameTextBox.Focus();
        }

        /// <summary>
        /// 点击删除选中模型配置按钮 (只允许删除用户自定义新增的模型)
        /// </summary>
        private void DeleteConfigButton_Click(object sender, RoutedEventArgs e)
        {
            if (ModelSelectComboBox.SelectedItem is not ComboBoxItem selectedItem || selectedItem.Content == null) return;
            string selectedDisplayName = selectedItem.Content.ToString();

            // 内置模型判定
            bool isBuiltIn = selectedDisplayName == "DeepSeek 大模型" || 
                             selectedDisplayName == "小米大模型" || 
                             selectedDisplayName == "OpenAI (ChatGPT)" || 
                             selectedDisplayName == "Anthropic (Claude)" || 
                             selectedDisplayName == "Google (Gemini)" || 
                             selectedDisplayName == "自定义模型";

            if (isBuiltIn)
            {
                MessageBox.Show("系统内置配置，无法删除。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"确定要删除 API 配置 [{selectedDisplayName}] 吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var cfgToDelete = _config.ModelConfigs.Find(m => m.DisplayName == selectedDisplayName);
                if (cfgToDelete != null)
                {
                    _config.ModelConfigs.Remove(cfgToDelete);
                }

                // 切换模型
                ModelSelectComboBox.SelectionChanged -= ModelSelectComboBox_SelectionChanged;
                RefreshModelSelectComboBox();

                // 切换回 DeepSeek
                ModelSelectComboBox.SelectedIndex = 0;
                _config.SelectedModel = "DeepSeek 大模型";
                _lastSelectedModel = _config.SelectedModel;
                LoadModelConfigToUI(_config.SelectedModel);

                ModelSelectComboBox.SelectionChanged += ModelSelectComboBox_SelectionChanged;

                // 强制同步保存一次
                ConfigManager.SaveConfig(_config);
            }
        }

        /// <summary>
        /// 取消新建模型配置
        /// </summary>
        private void CancelModalButton_Click(object sender, RoutedEventArgs e)
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 确定新建模型配置
        /// </summary>
        private void ConfirmModalButton_Click(object sender, RoutedEventArgs e)
        {
            string newName = NewConfigNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                MessageBox.Show("配置名称不能为空！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 检查重名
            var exists = _config.ModelConfigs.Exists(m => m.DisplayName.Equals(newName, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                MessageBox.Show($"已存在名为 [{newName}] 的配置，请换个名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 保存当前模型的 UI 配置
            SaveModelConfigFromUI(_lastSelectedModel);

            // 建立新的 ModelConfig，并采用当前 UI 输入的内容作为初始模板，让用户快速修改
            var newConfig = new ModelConfig
            {
                DisplayName = newName,
                ApiUrl = ApiUrlTextBox.Text.Trim(),
                ApiKey = ApiKeyTextBox.Text.Trim(),
                ModelName = ModelNameTextBox.Text.Trim()
            };

            _config.ModelConfigs.Add(newConfig);

            // 刷新下拉框
            ModelSelectComboBox.SelectionChanged -= ModelSelectComboBox_SelectionChanged;
            RefreshModelSelectComboBox();

            // 选中新添加的项
            for (int i = 0; i < ModelSelectComboBox.Items.Count; i++)
            {
                if (ModelSelectComboBox.Items[i] is ComboBoxItem item && item.Content?.ToString() == newName)
                {
                    ModelSelectComboBox.SelectedIndex = i;
                    break;
                }
            }

            _config.SelectedModel = newName;
            _lastSelectedModel = newName;
            LoadModelConfigToUI(newName);

            ModelSelectComboBox.SelectionChanged += ModelSelectComboBox_SelectionChanged;

            // 隐藏 Overlay
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 支持拖拽自定义无边框标题栏
        /// </summary>
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        /// <summary>
        /// 自定义最小化按钮点击事件
        /// </summary>
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        /// <summary>
        /// 自定义关闭按钮点击事件：隐藏窗口到后台托盘
        /// </summary>
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }
    }
}