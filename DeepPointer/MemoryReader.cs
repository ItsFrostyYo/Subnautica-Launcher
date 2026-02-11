using System;
using System.Diagnostics;
using System.Text;
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

        public static bool ReadInt32(Process process, IntPtr address, out int value)
        {
            value = 0;
            byte[] buffer = new byte[4];

            if (!ReadProcessMemory(process.Handle, address, buffer, 4, out _))
                return false;

            value = BitConverter.ToInt32(buffer, 0);
            return true;
        }

        public static bool ReadUInt16(Process process, IntPtr address, out ushort value)
        {
            value = 0;
            byte[] buffer = new byte[2];

            if (!ReadProcessMemory(process.Handle, address, buffer, 2, out _))
                return false;

            value = BitConverter.ToUInt16(buffer, 0);
            return true;
        }

        public static bool ReadIntPtr(Process process, IntPtr address, out IntPtr value)
        {
            value = IntPtr.Zero;
            byte[] buffer = new byte[IntPtr.Size];

            if (!ReadProcessMemory(process.Handle, address, buffer, buffer.Length, out _))
                return false;

            value = IntPtr.Size == 8
                ? new IntPtr(BitConverter.ToInt64(buffer, 0))
                : new IntPtr(BitConverter.ToInt32(buffer, 0));

            return true;
        }

        public static bool ReadBytes(Process process, IntPtr address, int count, out byte[] bytes)
        {
            bytes = new byte[count];
            return ReadProcessMemory(process.Handle, address, bytes, count, out _);
        }

        public static string ReadUtf8String(Process process, IntPtr address, int maxBytes = 256)
        {
            if (address == IntPtr.Zero || maxBytes <= 0)
                return string.Empty;

            if (!ReadBytes(process, address, maxBytes, out var buffer))
                return string.Empty;

            int end = Array.IndexOf(buffer, (byte)0);
            if (end < 0)
                end = buffer.Length;

            return Encoding.UTF8.GetString(buffer, 0, end);
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
