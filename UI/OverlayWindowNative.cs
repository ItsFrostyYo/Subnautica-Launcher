using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SubnauticaLauncher.UI
{
    internal static class OverlayWindowNative
    {
        private const int GwlExStyle = -20;
        private const int WsExTransparent = 0x20;
        private const int WsExToolWindow = 0x80;
        private const int WsExAppWindow = 0x00040000;
        private const int WsExNoActivate = 0x08000000;
        private const uint WdaNone = 0x00000000;

        public static void MakeClickThrough(Window window, bool preferPrimaryCaptureTarget = true)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            IntPtr exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
            long updated = exStyle.ToInt64()
                | WsExTransparent;

            if (preferPrimaryCaptureTarget)
            {
                updated |= WsExAppWindow;
                updated &= ~WsExToolWindow;
            }
            else
            {
                updated |= WsExToolWindow;
                updated &= ~WsExAppWindow;
            }

            updated &= ~WsExNoActivate;

            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(updated));
            SetWindowDisplayAffinity(hwnd, WdaNone);
        }

        private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(hWnd, nIndex)
                : new IntPtr(GetWindowLong32(hWnd, nIndex));
        }

        private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
                : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
    }
}
