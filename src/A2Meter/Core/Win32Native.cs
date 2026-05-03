using System;
using System.Runtime.InteropServices;

namespace A2Meter.Core;

internal static class Win32Native
{
    public const int WS_EX_LAYERED   = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const int WS_EX_NOACTIVATE  = 0x08000000;
    public const int WS_EX_TOPMOST     = 0x00000008;

    public const int GWL_EXSTYLE = -20;
    public const int LWA_ALPHA   = 0x2;

    public const int WM_NCHITTEST = 0x0084;
    public const int HTTRANSPARENT = -1;
    public const int HTCLIENT      = 1;
    public const int HTCAPTION     = 2;
    public const int HTLEFT        = 10;
    public const int HTRIGHT       = 11;
    public const int HTTOP         = 12;
    public const int HTTOPLEFT     = 13;
    public const int HTTOPRIGHT    = 14;
    public const int HTBOTTOM      = 15;
    public const int HTBOTTOMLEFT  = 16;
    public const int HTBOTTOMRIGHT = 17;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
}
