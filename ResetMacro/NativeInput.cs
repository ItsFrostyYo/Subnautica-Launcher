using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Drawing;

namespace SubnauticaLauncher.Macros
{
    public static class NativeInput
    {
        public static async Task Click(Point p, int delayMs = 10)
        {
            SetCursorPos(p.X, p.Y);
            MouseDown();
            await Task.Delay(5);
            MouseUp();
            await Task.Delay(delayMs);
        }

        public static void PressEsc()
        {
            keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
            keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // ================= NATIVE =================

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(
            uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(
            byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private static void MouseDown()
            => mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);

        private static void MouseUp()
            => mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
    }
}