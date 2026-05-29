using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Application = System.Windows.Application;


namespace SpeedTranslate
{
    public partial class TranslationStatusWindow : Window
    {
        // Win32 API 用于设置窗口鼠标穿透和不激活
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        public TranslationStatusWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 获取窗口句柄
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            // 设置鼠标穿透 (WS_EX_TRANSPARENT) 和不激活 (WS_EX_NOACTIVATE)
            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);

            // 开始呼吸灯动画
            if (FindResource("PulseStoryboard") is Storyboard storyboard)
            {
                storyboard.Begin(this);
            }
        }

        /// <summary>
        /// 显示悬浮窗并自动定位到鼠标光标处
        /// </summary>
        public void ShowAtCursor()
        {
            Reset();

            if (GetCursorPos(out POINT point))
            {
                // 获取当前主显示器的 DPI 缩放
                double scaleX = 1.0;
                double scaleY = 1.0;

                var source = PresentationSource.FromVisual(Application.Current.MainWindow);
                if (source?.CompositionTarget != null)
                {
                    scaleX = source.CompositionTarget.TransformToDevice.M11;
                    scaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                // 将物理像素坐标转换为 WPF 逻辑像素坐标，并在鼠标右下方偏移 25 像素
                this.Left = (point.X / scaleX) + 25;
                this.Top = (point.Y / scaleY) + 25;
            }

            this.Opacity = 0;
            this.Show();

            // 淡入动画
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            this.BeginAnimation(Window.OpacityProperty, fadeIn);
        }

        /// <summary>
        /// 恢复初始翻译状态样式
        /// </summary>
        private void Reset()
        {
            SpinnerPath.Visibility = Visibility.Visible;
            StatusTextBlock.Text = "AI 正在翻译中...";
            StatusTextBlock.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#E2E8F0");
        }

        /// <summary>
        /// 发生错误时，显示错误信息并于1.5秒后自动淡出
        /// </summary>
        /// <param name="errorMessage"></param>
        public void ShowError(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                SpinnerPath.Visibility = Visibility.Collapsed;
                StatusTextBlock.Text = errorMessage;
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Tomato;

                // 1.5 秒后自动淡出
                Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(() => HideWithFade()));
            });
        }

        /// <summary>
        /// 隐藏悬浮窗（带淡出动画）
        /// </summary>
        public void HideWithFade()
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => this.Hide();
            this.BeginAnimation(Window.OpacityProperty, fadeOut);
        }
    }
}
