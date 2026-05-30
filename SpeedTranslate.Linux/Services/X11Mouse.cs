using System;
using System.Runtime.InteropServices;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// 通过 X11 XQueryPointer 获取鼠标全局坐标。
/// </summary>
internal static class X11Mouse
{
    private const string LibX11 = "libX11.so.6";

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XQueryPointer(
        IntPtr display, IntPtr w,
        out IntPtr rootReturn, out IntPtr childReturn,
        out int rootX, out int rootY,
        out int winX, out int winY,
        out uint maskReturn);

    public static (int X, int Y) GetPosition()
    {
        var dpy = XOpenDisplay(IntPtr.Zero);
        if (dpy == IntPtr.Zero) return (0, 0);
        try
        {
            var root = XDefaultRootWindow(dpy);
            XQueryPointer(dpy, root, out _, out _, out var rx, out var ry, out _, out _, out _);
            return (rx, ry);
        }
        catch
        {
            return (0, 0);
        }
        finally
        {
            XCloseDisplay(dpy);
        }
    }
}
