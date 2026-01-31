using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace SubnauticaLauncher.Memory
{
    public sealed class DeepPointer
    {
        private readonly string _module;
        private readonly int[] _offsets;

        public DeepPointer(string module, params int[] offsets)
        {
            _module = module;
            _offsets = offsets;
        }

        public bool Deref(Process process, out IntPtr address)
        {
            address = IntPtr.Zero;

            var module = process.Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(m =>
                    m.ModuleName.Equals(_module, StringComparison.OrdinalIgnoreCase));

            if (module == null)
                return false;

            address = module.BaseAddress;

            for (int i = 0; i < _offsets.Length; i++)
            {
                address = IntPtr.Add(address, _offsets[i]);

                if (i < _offsets.Length - 1)
                {
                    if (!ReadPointer(process, address, out address))
                        return false;
                }
            }

            return true;
        }

        private static bool ReadPointer(Process process, IntPtr address, out IntPtr result)
        {
            result = IntPtr.Zero;

            byte[] buffer = new byte[IntPtr.Size];
            if (!ReadProcessMemory(process.Handle, address, buffer, buffer.Length, out _))
                return false;

            result = IntPtr.Size == 8
                ? new IntPtr(BitConverter.ToInt64(buffer, 0))
                : new IntPtr(BitConverter.ToInt32(buffer, 0));

            return result != IntPtr.Zero;
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