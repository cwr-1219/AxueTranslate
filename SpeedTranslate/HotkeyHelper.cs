using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace SpeedTranslate
{
    public static class HotkeyHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Win32 消息常量
        public const int WM_HOTKEY = 0x0312;

        /// <summary>
        /// 注册全局快捷键
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="id">热键唯一ID</param>
        /// <param name="modifiers">修饰键</param>
        /// <param name="key">普通按键</param>
        /// <returns>是否成功</returns>
        public static bool Register(IntPtr hWnd, int id, ModifierKeys modifiers, Key key)
        {
            uint fsModifiers = (uint)modifiers;
            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            // 确保先反注册旧的热键
            Unregister(hWnd, id);

            return RegisterHotKey(hWnd, id, fsModifiers, vk);
        }

        /// <summary>
        /// 注销全局快捷键
        /// </summary>
        /// <param name="hWnd">窗口句柄</param>
        /// <param name="id">热键唯一ID</param>
        public static bool Unregister(IntPtr hWnd, int id)
        {
            return UnregisterHotKey(hWnd, id);
        }
    }
}
