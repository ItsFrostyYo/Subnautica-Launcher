using System.Runtime.InteropServices;

namespace SubnauticaLauncher.Services;

internal static class InputReleaseService
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseXUp = 0x0100;

    public static void ReleaseAllInjectedInput()
    {
        try
        {
            var inputs = new List<Input>(252);
            for (ushort virtualKey = 0x08; virtualKey <= 0xFE; virtualKey++)
            {
                inputs.Add(new Input
                {
                    Type = InputKeyboard,
                    Data = new InputUnion
                    {
                        Keyboard = new KeyboardInput { VirtualKey = virtualKey, Flags = KeyEventKeyUp }
                    }
                });
            }

            inputs.Add(Mouse(MouseLeftUp | MouseRightUp | MouseMiddleUp));
            inputs.Add(Mouse(MouseXUp, 1));
            inputs.Add(Mouse(MouseXUp, 2));
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
        }
        catch
        {
            // Shutdown cleanup must continue even if Windows rejects synthetic input.
        }
    }

    private static Input Mouse(uint flags, uint data = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion { Mouse = new MouseInput { MouseData = data, Flags = flags } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);
}
