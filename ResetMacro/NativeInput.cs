using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

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
            KeyController.HoldStart(VK_ESCAPE);
            Thread.Sleep(50);
            KeyController.HoldStop(VK_ESCAPE);
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

    class KeyController
    {
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;    // Scancode
            public uint dwFlags;    // SCANCODE / KEYUP / EXTENDED
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy, mouseData; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);
        const uint MAPVK_VK_TO_VSC = 0;

        [DllImport("user32.dll")] static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);
        const uint SPI_GETKEYBOARDDELAY = 0x0016;  // 0..3  -> (idx+1)*250ms
        const uint SPI_GETKEYBOARDSPEED = 0x000A;  // 0..31 -> ~2.5..31 cps

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint KEYEVENTF_SCANCODE = 0x0008;

        static readonly HashSet<ushort> VkOnly = new()
        {
            0xAE, 0xAF, 0xAD,
            0xB0, 0xB1, 0xB2, 0xB3,
            0xA6,0xA7,0xA8,0xA9,0xAA,0xAB,0xAC,
            0xB4,0xB5,0xB6,0xB7
        };

        static INPUT MakeVkKey(ushort vk, bool keyUp) => new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = (keyUp ? KEYEVENTF_KEYUP : 0) | KEYEVENTF_EXTENDEDKEY,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        static INPUT MakeScanKey(ushort vk, bool keyUp)
        {
            ushort sc = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            bool extended = IsExtended(vk);
            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = sc,
                        dwFlags = KEYEVENTF_SCANCODE
                                  | (extended ? KEYEVENTF_EXTENDEDKEY : 0)
                                  | (keyUp ? KEYEVENTF_KEYUP : 0),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        static INPUT MakeKey(ushort vk, bool keyUp)
            => VkOnly.Contains(vk) ? MakeVkKey(vk, keyUp) : MakeScanKey(vk, keyUp);

        static bool IsExtended(ushort vk) =>
            vk is 0x25 or 0x26 or 0x27 or 0x28
            or 0x2D or 0x2E or 0x24 or 0x23
            or 0x21 or 0x22
            or 0x6F
            or 0xA3 or 0xA5;

        static void Send(params INPUT[] inputs) =>
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        static (int initialDelayMs, double repeatHz) GetSystemRepeatSettings()
        {
            int initialDelayMs = 300;
            if (SystemParametersInfo(SPI_GETKEYBOARDDELAY, 0, out uint delayIdx, 0))
                initialDelayMs = (int)((delayIdx + 1) * 250);

            double repeatHz = 25.0;
            if (SystemParametersInfo(SPI_GETKEYBOARDSPEED, 0, out uint speedIdx, 0))
            {
                repeatHz = 2.5 + (speedIdx * (31.0 - 2.5) / 31.0);
            }
            return (initialDelayMs, repeatHz);
        }

        class HoldState
        {
            public ushort Vk;
            public bool TapMode;
            public bool LeftDown;
            public int InitialDelayMs;
            public double RepeatHz;
            public CancellationTokenSource Cts = new();
            public Task? Worker;
        }

        static readonly ConcurrentDictionary<ushort, HoldState> Holds = new();

        public static void HoldStart(ushort vk, int? initialDelayMs = null, int? repeatHz = null)
        {
            if (Holds.ContainsKey(vk)) return;

            var (sysDelayMs, sysHz) = GetSystemRepeatSettings();
            bool tap = VkOnly.Contains(vk);

            var state = new HoldState
            {
                Vk = vk,
                TapMode = tap,
                InitialDelayMs = initialDelayMs ?? sysDelayMs,
                RepeatHz = repeatHz.HasValue ? Math.Max(1, (double)repeatHz.Value) : sysHz
            };
            if (!Holds.TryAdd(vk, state)) return;

            var token = state.Cts.Token;

            if (state.TapMode)
            {
                Send(MakeKey(vk, false), MakeKey(vk, true));
                state.LeftDown = false;
            }
            else
            {
                Send(MakeKey(vk, false));
                state.LeftDown = true;
            }

            state.Worker = Task.Run(() =>
            {
                if (token.WaitHandle.WaitOne(state.InitialDelayMs)) return;

                int periodMs = Math.Max(5, (int)Math.Round(1000.0 / state.RepeatHz));

                while (!token.IsCancellationRequested)
                {
                    if (token.WaitHandle.WaitOne(periodMs)) break;

                    if (state.TapMode)
                    {
                        Send(MakeKey(vk, false), MakeKey(vk, true));
                    }
                    else
                    {
                        Send(MakeKey(vk, false));
                    }
                }
            }, token);
        }

        public static void HoldStop(ushort vk)
        {
            if (!Holds.TryRemove(vk, out var state)) return;

            try
            {
                state.Cts.Cancel();
                state.Worker?.Wait(300);
            }
            catch { }

            if (!state.TapMode && state.LeftDown)
                Send(MakeKey(vk, true));

            state.Cts.Dispose();
        }

        public static void StopHoldingAllKeys()
        {
            foreach (var vk in new List<ushort>(Holds.Keys))
                HoldStop(vk);
        }
    }

    public sealed class KeyGroup
    {
        public KeyGroup(string name, IEnumerable<KeyCode> items)
        {
            Name = name;
            Items = new ObservableCollection<KeyCode>(items);
        }
        public string Name { get; }
        public ObservableCollection<KeyCode> Items { get; }
    }

    public class KeyCode
    {
        public uint Code { get; set; }
        public string Label { get; set; }
        public KeyCode(uint code, string label)
        {
            Code = code;
            Label = label;
        }
    }
}