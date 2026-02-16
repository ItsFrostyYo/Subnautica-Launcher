using System.Diagnostics;

namespace SubnauticaLauncher.Memory
{
    public static class DeepPointerFloatExtensions
    {
        public static bool TryReadFloat(
            this DeepPointer ptr,
            Process process,
            out float value)
        {
            value = 0f;

            if (!ptr.Deref(process, out var address))
                return false;

            return MemoryReader.ReadFloat(process, address, out value);
        }
    }
}
