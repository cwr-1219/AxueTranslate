using System;
using System.Runtime.InteropServices;

namespace SpeedTranslate.Linux.Services;

/// <summary>
/// X11 input shape extension PInvoke for click-through windows.
/// 通过 XShapeCombineRectangles + 空矩形数组让窗口完全不响应鼠标事件。
/// </summary>
internal static class X11InputShape
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXext = "libXext.so.6";

    private const int ShapeBounding = 0;
    private const int ShapeInput = 2;
    private const int ShapeSet = 0;
    private const int Unsorted = 0;

    [DllImport(LibX11)]
    private static extern IntPtr XOpenDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XCloseDisplay(IntPtr display);

    [DllImport(LibX11)]
    private static extern int XFlush(IntPtr display);

    [DllImport(LibXext)]
    private static extern void XShapeCombineRectangles(
        IntPtr display, IntPtr window, int destKind,
        int xOffset, int yOffset,
        IntPtr rectangles, int nRectangles, int op, int ordering);

    /// <summary>
    /// 让指定 X11 窗口对鼠标完全透明（穿透）。
    /// </summary>
    public static bool MakeClickThrough(IntPtr xWindow)
    {
        if (xWindow == IntPtr.Zero) return false;

        var dpy = XOpenDisplay(IntPtr.Zero);
        if (dpy == IntPtr.Zero) return false;

        try
        {
            // 传入空矩形数组 -> 窗口的 input region 变成空 -> 整窗都不响应鼠标
            XShapeCombineRectangles(dpy, xWindow, ShapeInput, 0, 0, IntPtr.Zero, 0, ShapeSet, Unsorted);
            XFlush(dpy);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"X11InputShape failed: {ex.Message}");
            return false;
        }
        finally
        {
            XCloseDisplay(dpy);
        }
    }
}
