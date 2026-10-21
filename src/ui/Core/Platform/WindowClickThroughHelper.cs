using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace LiveTranslateOverlay.Core.Platform;

public static class WindowClickThroughHelper
{
    private const int GwlExStyle = -20;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void EnableClickThrough(Window window)
    {
        if (!OperatingSystem.IsWindows()) return;

        IntPtr? handle = window.TryGetPlatformHandle()?.Handle;
        if (handle is { } hWnd && hWnd != IntPtr.Zero)
        {
            int style = GetWindowLong(hWnd, GwlExStyle);
            SetWindowLong(hWnd, GwlExStyle, style | WsExLayered | WsExTransparent);
        }
    }
}