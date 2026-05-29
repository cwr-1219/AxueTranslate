using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Forms;
using Point = System.Drawing.Point;
using FontStyle = System.Drawing.FontStyle;
using Font = System.Drawing.Font;


namespace SpeedTranslate
{
    public class TrayIconManager : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private readonly Window _mainWindow;

        public TrayIconManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
        }

        public void Initialize()
        {
            _notifyIcon = new NotifyIcon();
            _notifyIcon.Text = "AxueTranslate";
            
            // 动态绘制一个精致的托盘图标
            _notifyIcon.Icon = CreateTrayIcon();
            _notifyIcon.Visible = true;

            // 双击托盘显示主窗口
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();

            // 右键菜单
            var contextMenu = new ContextMenuStrip();
            
            var showItem = new ToolStripMenuItem("显示设置");
            showItem.Click += (s, e) => ShowMainWindow();
            contextMenu.Items.Add(showItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出程序");
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void ShowMainWindow()
        {
            _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>
        /// 动态在内存中绘制一个带有紫色渐变和白色“译”字的精致图标
        /// </summary>
        private Icon CreateTrayIcon()
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    // 绘制圆形渐变背景 (Indigo to Purple)
                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        new Point(0, 0), new Point(32, 32),
                        Color.FromArgb(99, 102, 241),   // Indigo (#6366F1)
                        Color.FromArgb(168, 85, 247)))  // Purple (#A855F7)
                    {
                        g.FillEllipse(brush, 2, 2, 28, 28);
                    }

                    // 绘制中心白色“译”字
                    using (System.Drawing.Font font = new System.Drawing.Font("Microsoft YaHei", 13, FontStyle.Bold))
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    {
                        StringFormat sf = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        };
                        // 略微调整文本框位置以确保完美视觉居中
                        g.DrawString("译", font, textBrush, new RectangleF(1, 2.5f, 30, 30), sf);
                    }
                }

                // 从 Bitmap 句柄创建 Icon
                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }
    }
}
