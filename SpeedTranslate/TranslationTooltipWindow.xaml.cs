using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace SpeedTranslate
{
    public partial class TranslationTooltipWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Win32Point lppoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Point
        {
            public int X;
            public int Y;
        }

        private bool _isClosing = false;
        private string _translatedText = "";
        private AppConfig? _config;
        private LLMService? _llmService;
        private string _originalText = "";

        // 锚点逻辑坐标，记录首次划词定位，防止大小改变或翻译返回时浮窗随当前鼠标乱跑
        private double _anchorLogicalX = -1;
        private double _anchorLogicalY = -1;

        public TranslationTooltipWindow()
        {
            InitializeComponent();
            this.SizeChanged += TranslationTooltipWindow_SizeChanged;
        }

        /// <summary>
        /// 弹窗展示翻译内容，并自动定位到鼠标光标处
        /// </summary>
        public void ShowTooltip(string originalText, string translatedText, AppConfig config, LLMService llmService)
        {
            _anchorLogicalX = -1;
            _anchorLogicalY = -1;

            _translatedText = translatedText;
            _config = config;
            _llmService = llmService;
            _originalText = originalText;
            
            // 回填内容
            TranslatedTextBlock.Text = translatedText;
            ModelTagText.Text = $"划词翻译 ({config.SelectedModel})";

            if (!string.IsNullOrWhiteSpace(originalText) && originalText.Length < 120)
            {
                OriginalTextBlock.Text = originalText;
                OriginalTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                OriginalTextBlock.Visibility = Visibility.Collapsed;
            }

            // 初始化语种下拉框选中状态（解绑后再绑定）
            LanguageComboBox.SelectionChanged -= LanguageComboBox_SelectionChanged;
            foreach (System.Windows.Controls.ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == _config.TargetLanguage)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }
            LanguageComboBox.SelectionChanged += LanguageComboBox_SelectionChanged;

            // 初始化位置（在 Show 之前，先移出屏幕外以防一瞬间的闪烁，待大小计算后再精确定位）
            this.Left = -9999;
            this.Top = -9999;
            this.Opacity = 0;
            this.Show();
            
            // 定位
            PositionAtMouse();
        }

        /// <summary>
        /// 定位窗口在当前鼠标偏右下方，并结合 DPI 和屏幕边缘进行智能防护
        /// </summary>
        private void PositionAtMouse()
        {
            double logicalX;
            double logicalY;

            if (_anchorLogicalX >= 0 && _anchorLogicalY >= 0)
            {
                logicalX = _anchorLogicalX;
                logicalY = _anchorLogicalY;
            }
            else
            {
                if (!GetCursorPos(out Win32Point win32Point)) return;

                double dpiScaleX = 1.0;
                double dpiScaleY = 1.0;
                
                // 从当前视觉源获取 DPI 缩放比例
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                // 物理坐标 -> 逻辑 DIP 坐标
                logicalX = win32Point.X / dpiScaleX;
                logicalY = win32Point.Y / dpiScaleY;

                // 暂存作为固定锚点，防止窗口尺寸自适应拉伸或重新翻译时浮窗乱跑
                _anchorLogicalX = logicalX;
                _anchorLogicalY = logicalY;
            }

            // 偏右下方
            double left = logicalX + 12;
            double top = logicalY + 18;

            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;

            // 边缘防护 1: 右边缘越界，则整体偏左浮动
            if (left + this.ActualWidth > screenWidth)
            {
                left = logicalX - this.ActualWidth - 10;
            }
            // 边缘防护 2: 下边缘越界，则整体向上浮动
            if (top + this.ActualHeight > screenHeight)
            {
                top = logicalY - this.ActualHeight - 10;
            }

            // 边界兜底限制
            if (left < 10) left = 10;
            if (top < 10) top = 10;

            this.Left = left;
            this.Top = top;
        }

        private void TranslationTooltipWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 每次内容拉伸/大小确定时，重新微调位置，确保精准
            PositionAtMouse();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 淡入动画
            if (FindResource("FadeInStory") is Storyboard sb)
            {
                sb.Begin(this);
            }
        }

        /// <summary>
        /// 优雅淡出关闭
        /// </summary>
        private async void FadeOutAndClose()
        {
            if (_isClosing) return;
            _isClosing = true;

            if (FindResource("FadeOutStory") is Storyboard sb)
            {
                sb.Begin(this);
                await Task.Delay(160); // 等待动画播放完成
            }
            this.Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            FadeOutAndClose();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // 鼠标点击其它外部程序失焦时，自动淡出关闭，体验极佳
            FadeOutAndClose();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // 按下 Esc 键时优雅关闭
            if (e.Key == Key.Escape)
            {
                FadeOutAndClose();
            }
        }

        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_translatedText)) return;

            try
            {
                System.Windows.Clipboard.SetText(_translatedText);
                
                // 1.5 秒交互反馈效果
                CopyButton.Content = "已复制 ✔";
                CopyButton.IsEnabled = false;
                
                await Task.Delay(1500);
                
                CopyButton.Content = "📋 复制";
                CopyButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"复制失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 异步更新翻译内容，并重新触发定位和边缘检测
        /// </summary>
        public void UpdateTranslatedText(string translatedText)
        {
            _translatedText = translatedText;
            TranslatedTextBlock.Text = translatedText;
            PositionAtMouse();
        }

        /// <summary>
        /// 切换翻译语种事件：同步全局配置，并自动重新触发翻译展示
        /// </summary>
        private async void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_config == null || _llmService == null || string.IsNullOrWhiteSpace(_originalText)) return;

            if (LanguageComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                string targetLang = item.Tag?.ToString() ?? "Auto";
                if (_config.TargetLanguage == targetLang) return; // 没变则不重复请求

                // 1. 同步修改全局配置并持久化保存
                _config.TargetLanguage = targetLang;
                ConfigManager.SaveConfig(_config);

                // 2. 双向联动：通知主界面 ComboBox 同步更改选中项
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.SyncLanguageSelection(targetLang);
                }

                // 3. UI 展现正在重新翻译
                TranslatedTextBlock.Text = "翻译中...";

                try
                {
                    // 4. 重新异步请求翻译
                    string translatedText = await _llmService.TranslateAsync(_originalText, _config);
                    UpdateTranslatedText(translatedText);
                }
                catch (Exception ex)
                {
                    UpdateTranslatedText($"翻译失败: {ex.Message}");
                }
            }
        }
    }
}
