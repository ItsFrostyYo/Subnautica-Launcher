using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SubnauticaLauncher.Memory
{
    public static class MemoryReader
    {
        public static bool ReadFloat(Process process, IntPtr address, out float value)
        {
            value = 0f;
            byte[] buffer = new byte[4];

            if (!ReadProcessMemory(process.Handle, address, buffer, 4, out _))
                return false;

            value = BitConverter.ToSingle(buffer, 0);
            return true;
        }

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(
            IntPtr hProcess,
            IntPtr lpBaseAddress,
            byte[] lpBuffer,
            int dwSize,
            out int lpNumberOfBytesRead);
    }
}