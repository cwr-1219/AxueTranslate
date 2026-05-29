using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SpeedTranslate
{
    public static class InputHelper
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL = 0x11;
        private const byte VK_C = 0x43;
        private const byte VK_V = 0x56;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        private const byte VK_SHIFT = 0x10;
        private const byte VK_HOME = 0x24;

        /// <summary>
        /// 模拟按下 Ctrl+C 并复制文本
        /// </summary>
        public static async Task SimulateCopyAsync()
        {
            // 延迟 150ms，等待用户释放他们自己按下的物理快捷键（如 Ctrl+Alt+T）
            // 否则物理按键的按下状态会干扰我们模拟的键盘事件
            await Task.Delay(150);

            // 模拟 Ctrl+C
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); // Press Ctrl
            keybd_event(VK_C, 0, 0, UIntPtr.Zero);       // Press C
            keybd_event(VK_C, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release C
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release Ctrl

            // 稍微延迟 100ms，确保系统剪贴板更新完成
            await Task.Delay(100);
        }

        /// <summary>
        /// 模拟按下 Ctrl+V 进行粘贴替换
        /// </summary>
        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        /// <summary>
        /// 模拟按下 Ctrl+V 进行粘贴替换，并确保焦点在目标窗口上
        /// </summary>
        public static async Task SimulatePasteAsync(IntPtr targetWindow)
        {
            if (targetWindow != IntPtr.Zero)
            {
                try
                {
                    SetForegroundWindow(targetWindow);
                }
                catch { }
                // 延迟 80ms，等待系统焦点切换和窗口绘制稳定
                await Task.Delay(80);
            }
            else
            {
                // 稍微延迟确保UI焦点恢复
                await Task.Delay(100);
            }

            // 模拟 Ctrl+V
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); // Press Ctrl
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);       // Press V
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release V
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release Ctrl

            // 再次延迟确保粘贴操作完成
            await Task.Delay(100);
        }

        private const byte VK_A = 0x41;

        /// <summary>
        /// 模拟按下 Ctrl + A 选中输入框内的全部内容
        /// </summary>
        public static async Task SimulateSelectAllAsync()
        {
            // 延迟 100ms，确保系统键盘输入缓冲区状态稳定
            await Task.Delay(100);

            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); // Press Ctrl
            keybd_event(VK_A, 0, 0, UIntPtr.Zero);       // Press A
            keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);  // Release A
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Release Ctrl

            // 再次延迟 100ms，确保系统完全捕获选中状态
            await Task.Delay(100);
        }
    }
}
