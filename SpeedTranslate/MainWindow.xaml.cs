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
            _lastSelectedModel = _config.SelectedModel;
            if (_config.SelectedModel == "DeepSeek")
            {
                ModelSelectComboBox.SelectedIndex = 0;
            }
            else if (_config.SelectedModel == "XiaoMi")
            {
                ModelSelectComboBox.SelectedIndex = 1;
            }
            else
            {
                ModelSelectComboBox.SelectedIndex = 2;
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
            string selectedModel = selectedItem.Content.ToString() == "小米大模型" ? "XiaoMi" : 
                                   selectedItem.Content.ToString() == "自定义模型" ? "Custom" : "DeepSeek";
            
            _lastSelectedModel = selectedModel;

            // 3. 将新选中的模型配置加载到 UI 输入框
            LoadModelConfigToUI(selectedModel);
        }

        /// <summary>
        /// 从 UI 抓取配置保存到内存实体中
        /// </summary>
        private void SaveModelConfigFromUI(string model)
        {
            if (model == "DeepSeek")
            {
                _config.DeepSeekUrl = ApiUrlTextBox.Text.Trim();
                _config.DeepSeekApiKey = ApiKeyTextBox.Text.Trim();
                _config.DeepSeekModel = ModelNameTextBox.Text.Trim();
            }
            else if (model == "XiaoMi")
            {
                _config.XiaoMiUrl = ApiUrlTextBox.Text.Trim();
                _config.XiaoMiApiKey = ApiKeyTextBox.Text.Trim();
                _config.XiaoMiModel = ModelNameTextBox.Text.Trim();
            }
            else
            {
                _config.CustomUrl = ApiUrlTextBox.Text.Trim();
                _config.CustomApiKey = ApiKeyTextBox.Text.Trim();
                _config.CustomModel = ModelNameTextBox.Text.Trim();
            }
        }

        /// <summary>
        /// 将指定模型的配置加载到 UI
        /// </summary>
        private void LoadModelConfigToUI(string model)
        {
            if (model == "DeepSeek")
            {
                ApiUrlTextBox.Text = string.IsNullOrWhiteSpace(_config.DeepSeekUrl) ? "https://api.deepseek.com/v1" : _config.DeepSeekUrl;
                ApiKeyTextBox.Text = _config.DeepSeekApiKey;
                ModelNameTextBox.Text = string.IsNullOrWhiteSpace(_config.DeepSeekModel) ? "deepseek-chat" : _config.DeepSeekModel;
            }
            else if (model == "XiaoMi")
            {
                ApiUrlTextBox.Text = _config.XiaoMiUrl;
                ApiKeyTextBox.Text = _config.XiaoMiApiKey;
                ModelNameTextBox.Text = _config.XiaoMiModel;
            }
            else
            {
                ApiUrlTextBox.Text = _config.CustomUrl;
                ApiKeyTextBox.Text = _config.CustomApiKey;
                ModelNameTextBox.Text = _config.CustomModel;
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
                string selectedModelName = selectedItem.Content.ToString() == "小米大模型" ? "XiaoMi" : 
                                           selectedItem.Content.ToString() == "自定义模型" ? "Custom" : "DeepSeek";
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

                // 创建并弹出 ContextMenu
                var contextMenu = new ContextMenu();
                
                // 给弹出菜单定制精致暗黑主题样式
                var menuStyle = new Style(typeof(ContextMenu));
                menuStyle.Setters.Add(new Setter(ContextMenu.BackgroundProperty, (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#161224")));
                menuStyle.Setters.Add(new Setter(ContextMenu.BorderBrushProperty, (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#3D355C")));
                contextMenu.Style = menuStyle;

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
                WriteErrorLog("获取可用模型列表失败", ex);
                MessageBox.Show($"获取模型列表失败！\n原因: {ex.Message}\n详细信息已记录到日志 error.log。", "获取失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FetchModelsButton.IsEnabled = true;
                FetchModelsButton.Content = "切换模型";
            }
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