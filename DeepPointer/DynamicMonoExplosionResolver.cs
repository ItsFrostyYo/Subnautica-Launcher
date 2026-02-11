using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using SubnauticaLauncher.Memory;

namespace SubnauticaLauncher.Explosion
{
    public sealed class DynamicMonoExplosionResolver : IExplosionResolver
    {
        private const ushort FieldAttributeStatic = 0x10;
        private const int InitRetryMs = 3000;
        private const uint DONT_RESOLVE_DLL_REFERENCES = 0x00000001;

        private static readonly MonoLayout LayoutV1 = new(
            assemblyImage: 0x58,
            imageAssemblyName: 0x28,
            imageClassCache: 0x3D0,
            hashSize: 0x18,
            hashTable: 0x20,
            className: 0x48,
            classFields: 0xA8,
            classFieldCount: 0x94,
            classParent: 0x30,
            classRuntimeInfo: 0xF8,
            classVTableSize: 0x5C,
            classNextClassCache: 0x100,
            classDefFieldCount: 0x94,
            classDefNextClassCache: 0x100,
            classFieldName: 0x8,
            classFieldParent: 0x10,
            classFieldOffset: 0x18,
            classFieldType: 0x0,
            monoTypeAttrs: 0x8,
            runtimeInfoDomainVTables: 0x8,
            vtableData: 0x18,
            vtableVtable: -1);

        private static readonly MonoLayout LayoutV2 = new(
            assemblyImage: 0x60,
            imageAssemblyName: 0x28,
            imageClassCache: 0x4C0,
            hashSize: 0x18,
            hashTable: 0x20,
            className: 0x48,
            classFields: 0x98,
            classFieldCount: -1,
            classParent: 0x30,
            classRuntimeInfo: 0xD0,
            classVTableSize: 0x5C,
            classNextClassCache: -1,
            classDefFieldCount: 0x100,
            classDefNextClassCache: 0x108,
            classFieldName: 0x8,
            classFieldParent: 0x10,
            classFieldOffset: 0x18,
            classFieldType: 0x0,
            monoTypeAttrs: 0x8,
            runtimeInfoDomainVTables: 0x8,
            vtableData: -1,
            vtableVtable: 0x40);

        private readonly object _sync = new();
        private readonly IExplosionResolver _fallback;
        private readonly DeepPointer? _x;
        private readonly DeepPointer? _y;
        private readonly DeepPointer? _z;
        private readonly int _classFieldSize;

        private bool _ready;
        private bool _loggedInitFailure;
        private int _pid = -1;
        private DateTime _nextInitAttemptUtc = DateTime.MinValue;

        private MonoFlavor _flavor;
        private MonoLayout _layout = LayoutV1;
        private IntPtr _staticDataBase = IntPtr.Zero;
        private int _mainStaticOffset;
        private int _countdownOffset;
        private int _warningOffset;

        public DynamicMonoExplosionResolver(
            IExplosionResolver fallback,
            DeepPointer? x = null,
            DeepPointer? y = null,
            DeepPointer? z = null)
        {
            _fallback = fallback;
            _x = x;
            _y = y;
            _z = z;

            // MonoClassField: ptr + ptr + ptr + int (+ alignment)
            int rawSize = IntPtr.Size * 3 + 4;
            _classFieldSize = Align(rawSize, IntPtr.Size);
        }

        public bool TryRead(Process proc, out ExplosionSnapshot s)
        {
            s = new ExplosionSnapshot();

            ReadPosition(proc, ref s);

            EnsureInitialized(proc);

            if (_ready && TryReadExplosion(proc, out float explosionTime))
            {
                s.ExplosionTime = explosionTime;
                return true;
            }

            if (_fallback.TryRead(proc, out var fallbackSnapshot))
            {
                s.ExplosionTime = fallbackSnapshot.ExplosionTime;
                if (s.PosX == 0f && s.PosY == 0f && s.PosZ == 0f)
                {
                    s.PosX = fallbackSnapshot.PosX;
                    s.PosY = fallbackSnapshot.PosY;
                    s.PosZ = fallbackSnapshot.PosZ;
                }
            }

            return true;
        }

        private void EnsureInitialized(Process proc)
        {
            lock (_sync)
            {
                if (_pid != proc.Id)
                {
                    ResetState(proc.Id);
                }

                if (_ready || DateTime.UtcNow < _nextInitAttemptUtc)
                {
                    return;
                }

                if (TryInitialize(proc))
                {
                    _ready = true;
                    _loggedInitFailure = false;
                    Logger.Log($"Dynamic mono explosion resolver initialized ({_flavor}).");
                    return;
                }

                _nextInitAttemptUtc = DateTime.UtcNow.AddMilliseconds(InitRetryMs);
                if (!_loggedInitFailure)
                {
                    Logger.Warn("Dynamic mono explosion resolver init failed. Falling back to static pointers.");
                    _loggedInitFailure = true;
                }
            }
        }

        private void ResetState(int pid)
        {
            _pid = pid;
            _ready = false;
            _loggedInitFailure = false;
            _nextInitAttemptUtc = DateTime.MinValue;
            _flavor = MonoFlavor.Unknown;
            _layout = LayoutV1;
            _staticDataBase = IntPtr.Zero;
            _mainStaticOffset = 0;
            _countdownOffset = 0;
            _warningOffset = 0;
        }

        private void ReadPosition(Process proc, ref ExplosionSnapshot s)
        {
            if (_x != null && _x.TryReadFloat(proc, out float x)) s.PosX = x;
            if (_y != null && _y.TryReadFloat(proc, out float y)) s.PosY = y;
            if (_z != null && _z.TryReadFloat(proc, out float z)) s.PosZ = z;
        }

        private bool TryReadExplosion(Process proc, out float explosionTime)
        {
            explosionTime = -1f;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(_staticDataBase, _mainStaticOffset), out var mainInstance))
            {
                return false;
            }

            if (mainInstance == IntPtr.Zero)
            {
                explosionTime = -1f;
                return true;
            }

            if (!MemoryReader.ReadFloat(proc, IntPtr.Add(mainInstance, _countdownOffset), out float countdown))
            {
                return false;
            }

            if (!MemoryReader.ReadFloat(proc, IntPtr.Add(mainInstance, _warningOffset), out float warning))
            {
                return false;
            }

            explosionTime = countdown > 0f && warning > 0f
                ? countdown - warning
                : -1f;

            return true;
        }

        private bool TryInitialize(Process proc)
        {
            if (!TryGetMonoModule(proc, out var monoModule, out var flavor) || monoModule == null)
            {
                return false;
            }

            _flavor = flavor;
            _layout = flavor == MonoFlavor.MonoV2 ? LayoutV2 : LayoutV1;

            IntPtr assemblyForeach = GetRemoteExportAddress(monoModule, "mono_assembly_foreach");
            if (assemblyForeach == IntPtr.Zero)
            {
                return false;
            }

            if (!TryFindAssembliesListPointer(proc, assemblyForeach, out IntPtr assembliesList))
            {
                return false;
            }

            IntPtr mainImage = FindAssemblyImage(proc, assembliesList, "Assembly-CSharp");
            if (mainImage == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exploderClass = FindClass(proc, mainImage, "CrashedShipExploder");
            if (exploderClass == IntPtr.Zero)
            {
                return false;
            }

            if (!TryFindStaticField(proc, exploderClass, "main", out int mainOffset, out IntPtr staticFieldParentClass))
            {
                return false;
            }

            if (!TryGetStaticDataAddress(proc, staticFieldParentClass, out IntPtr staticDataBase))
            {
                return false;
            }

            if (!TryFindFieldOffset(proc, exploderClass, "timeToStartCountdown", out int countdownOffset))
            {
                return false;
            }

            if (!TryFindFieldOffset(proc, exploderClass, "timeToStartWarning", out int warningOffset))
            {
                return false;
            }

            _staticDataBase = staticDataBase;
            _mainStaticOffset = mainOffset;
            _countdownOffset = countdownOffset;
            _warningOffset = warningOffset;

            return true;
        }

        private bool TryGetMonoModule(Process proc, out ProcessModule? module, out MonoFlavor flavor)
        {
            module = proc.Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("mono-2.0-bdwgc.dll", StringComparison.OrdinalIgnoreCase));

            if (module != null)
            {
                flavor = MonoFlavor.MonoV2;
                return true;
            }

            module = proc.Modules
                .Cast<ProcessModule>()
                .FirstOrDefault(m => m.ModuleName.Equals("mono.dll", StringComparison.OrdinalIgnoreCase));

            if (module != null)
            {
                flavor = MonoFlavor.MonoV1;
                return true;
            }

            flavor = MonoFlavor.Unknown;
            return false;
        }

        private static IntPtr GetRemoteExportAddress(ProcessModule module, string exportName)
        {
            IntPtr localModule = LoadLibraryEx(module.FileName, IntPtr.Zero, DONT_RESOLVE_DLL_REFERENCES);
            if (localModule == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                IntPtr localExport = GetProcAddress(localModule, exportName);
                if (localExport == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }

                long rva = localExport.ToInt64() - localModule.ToInt64();
                return new IntPtr(module.BaseAddress.ToInt64() + rva);
            }
            finally
            {
                FreeLibrary(localModule);
            }
        }

        private bool TryFindAssembliesListPointer(Process proc, IntPtr monoAssemblyForeach, out IntPtr assembliesList)
        {
            assembliesList = IntPtr.Zero;

            if (!MemoryReader.ReadBytes(proc, monoAssemblyForeach, 0x100, out var bytes))
            {
                return false;
            }

            int operandOffset = IntPtr.Size == 8
                ? FindPattern(bytes, new byte[] { 0x48, 0x8B, 0x0D })
                : FindPattern(bytes, new byte[] { 0xFF, 0x35 });

            if (operandOffset < 0)
            {
                return false;
            }

            IntPtr operandAddress = new(
                monoAssemblyForeach.ToInt64() + operandOffset + (IntPtr.Size == 8 ? 3 : 2));

            IntPtr globalAssembliesAddress;
            if (IntPtr.Size == 8)
            {
                if (!MemoryReader.ReadInt32(proc, operandAddress, out int rel))
                {
                    return false;
                }

                globalAssembliesAddress = new IntPtr(operandAddress.ToInt64() + 4 + rel);
            }
            else
            {
                if (!MemoryReader.ReadInt32(proc, operandAddress, out int abs))
                {
                    return false;
                }

                globalAssembliesAddress = new IntPtr(abs);
            }

            return MemoryReader.ReadIntPtr(proc, globalAssembliesAddress, out assembliesList);
        }

        private IntPtr FindAssemblyImage(Process proc, IntPtr assembliesList, string imageName)
        {
            IntPtr node = assembliesList;
            int guard = 0;

            while (node != IntPtr.Zero && guard++ < 4096)
            {
                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(node, MonoLayout.GListData), out var assembly) || assembly == IntPtr.Zero)
                {
                    break;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(assembly, _layout.AssemblyImage), out var image) || image == IntPtr.Zero)
                {
                    break;
                }

                if (MemoryReader.ReadIntPtr(proc, IntPtr.Add(image, _layout.ImageAssemblyName), out var imageNamePtr))
                {
                    string current = MemoryReader.ReadUtf8String(proc, imageNamePtr, 128);
                    if (current.Equals(imageName, StringComparison.OrdinalIgnoreCase))
                    {
                        return image;
                    }
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(node, MonoLayout.GListNext), out node))
                {
                    break;
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr FindClass(Process proc, IntPtr image, string className)
        {
            IntPtr classCache = IntPtr.Add(image, _layout.ImageClassCache);

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(classCache, _layout.HashSize), out int size))
            {
                return IntPtr.Zero;
            }

            if (size <= 0 || size > 200000)
            {
                return IntPtr.Zero;
            }

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(classCache, _layout.HashTable), out var table) || table == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            int pointerSize = IntPtr.Size;

            for (int i = 0; i < size; i++)
            {
                IntPtr bucketEntryAddress = new IntPtr(table.ToInt64() + (long)i * pointerSize);
                if (!MemoryReader.ReadIntPtr(proc, bucketEntryAddress, out var klass))
                {
                    continue;
                }

                int chainGuard = 0;
                while (klass != IntPtr.Zero && chainGuard++ < 10000)
                {
                    if (MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, _layout.ClassName), out var classNamePtr))
                    {
                        string currentName = MemoryReader.ReadUtf8String(proc, classNamePtr, 128);
                        if (currentName.Equals(className, StringComparison.Ordinal))
                        {
                            return klass;
                        }
                    }

                    int nextOffset = _flavor == MonoFlavor.MonoV2
                        ? _layout.ClassDefNextClassCache
                        : _layout.ClassNextClassCache;

                    if (nextOffset < 0 || !MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, nextOffset), out klass))
                    {
                        break;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private bool TryFindFieldOffset(Process proc, IntPtr klass, string fieldName, out int fieldOffset)
        {
            fieldOffset = 0;

            IntPtr current = klass;
            int parentGuard = 0;
            while (current != IntPtr.Zero && parentGuard++ < 32)
            {
                if (TryFindField(proc, current, fieldName, out fieldOffset, out _, out _))
                {
                    return true;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(current, _layout.ClassParent), out current))
                {
                    break;
                }
            }

            return false;
        }

        private bool TryFindStaticField(
            Process proc,
            IntPtr klass,
            string fieldName,
            out int staticFieldOffset,
            out IntPtr staticFieldParentClass)
        {
            staticFieldOffset = 0;
            staticFieldParentClass = IntPtr.Zero;

            if (!TryFindField(proc, klass, fieldName, out int off, out IntPtr parentClass, out IntPtr typePtr))
            {
                return false;
            }

            if (!MemoryReader.ReadUInt16(proc, IntPtr.Add(typePtr, _layout.MonoTypeAttrs), out ushort attrs))
            {
                return false;
            }

            if ((attrs & FieldAttributeStatic) == 0)
            {
                return false;
            }

            staticFieldOffset = off;
            staticFieldParentClass = parentClass;
            return true;
        }

        private bool TryFindField(
            Process proc,
            IntPtr klass,
            string fieldName,
            out int fieldOffset,
            out IntPtr fieldParentClass,
            out IntPtr fieldType)
        {
            fieldOffset = 0;
            fieldParentClass = IntPtr.Zero;
            fieldType = IntPtr.Zero;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, _layout.ClassFields), out var fields) || fields == IntPtr.Zero)
            {
                return false;
            }

            int countOffset = _flavor == MonoFlavor.MonoV2
                ? _layout.ClassDefFieldCount
                : _layout.ClassFieldCount;

            if (countOffset < 0 || !MemoryReader.ReadInt32(proc, IntPtr.Add(klass, countOffset), out int fieldCount))
            {
                return false;
            }

            if (fieldCount <= 0 || fieldCount > 2000)
            {
                return false;
            }

            for (int i = 0; i < fieldCount; i++)
            {
                IntPtr field = new(fields.ToInt64() + (long)i * _classFieldSize);

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(field, _layout.ClassFieldName), out var fieldNamePtr))
                {
                    continue;
                }

                string currentFieldName = MemoryReader.ReadUtf8String(proc, fieldNamePtr, 128);
                if (!currentFieldName.Equals(fieldName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MemoryReader.ReadInt32(proc, IntPtr.Add(field, _layout.ClassFieldOffset), out fieldOffset))
                {
                    return false;
                }

                MemoryReader.ReadIntPtr(proc, IntPtr.Add(field, _layout.ClassFieldParent), out fieldParentClass);
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(field, _layout.ClassFieldType), out fieldType);
                return true;
            }

            return false;
        }

        private bool TryGetStaticDataAddress(Process proc, IntPtr klass, out IntPtr staticData)
        {
            staticData = IntPtr.Zero;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, _layout.ClassRuntimeInfo), out var runtimeInfo) ||
                runtimeInfo == IntPtr.Zero)
            {
                return false;
            }

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(runtimeInfo, _layout.RuntimeInfoDomainVTables), out var classVTable) ||
                classVTable == IntPtr.Zero)
            {
                return false;
            }

            if (_flavor == MonoFlavor.MonoV1)
            {
                if (_layout.VTableData < 0)
                {
                    return false;
                }

                return MemoryReader.ReadIntPtr(proc, IntPtr.Add(classVTable, _layout.VTableData), out staticData);
            }

            if (_layout.VTableVTable < 0 ||
                !MemoryReader.ReadInt32(proc, IntPtr.Add(klass, _layout.ClassVTableSize), out int vtableSize))
            {
                return false;
            }

            long staticAddressPtr = classVTable.ToInt64() + _layout.VTableVTable + (long)vtableSize * IntPtr.Size;
            return MemoryReader.ReadIntPtr(proc, new IntPtr(staticAddressPtr), out staticData);
        }

        private static int FindPattern(byte[] haystack, byte[] pattern)
        {
            for (int i = 0; i <= haystack.Length - pattern.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (haystack[i + j] != pattern[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int Align(int value, int align)
        {
            int mod = value % align;
            return mod == 0 ? value : value + align - mod;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        private enum MonoFlavor
        {
            Unknown,
            MonoV1,
            MonoV2
        }

        private readonly struct MonoLayout
        {
            public MonoLayout(
                int assemblyImage,
                int imageAssemblyName,
                int imageClassCache,
                int hashSize,
                int hashTable,
                int className,
                int classFields,
                int classFieldCount,
                int classParent,
                int classRuntimeInfo,
                int classVTableSize,
                int classNextClassCache,
                int classDefFieldCount,
                int classDefNextClassCache,
                int classFieldName,
                int classFieldParent,
                int classFieldOffset,
                int classFieldType,
                int monoTypeAttrs,
                int runtimeInfoDomainVTables,
                int vtableData,
                int vtableVtable)
            {
                AssemblyImage = assemblyImage;
                ImageAssemblyName = imageAssemblyName;
                ImageClassCache = imageClassCache;
                HashSize = hashSize;
                HashTable = hashTable;
                ClassName = className;
                ClassFields = classFields;
                ClassFieldCount = classFieldCount;
                ClassParent = classParent;
                ClassRuntimeInfo = classRuntimeInfo;
                ClassVTableSize = classVTableSize;
                ClassNextClassCache = classNextClassCache;
                ClassDefFieldCount = classDefFieldCount;
                ClassDefNextClassCache = classDefNextClassCache;
                ClassFieldName = classFieldName;
                ClassFieldParent = classFieldParent;
                ClassFieldOffset = classFieldOffset;
                ClassFieldType = classFieldType;
                MonoTypeAttrs = monoTypeAttrs;
                RuntimeInfoDomainVTables = runtimeInfoDomainVTables;
                VTableData = vtableData;
                VTableVTable = vtableVtable;
            }

            public int AssemblyImage { get; }
            public int ImageAssemblyName { get; }
            public int ImageClassCache { get; }
            public int HashSize { get; }
            public int HashTable { get; }
            public int ClassName { get; }
            public int ClassFields { get; }
            public int ClassFieldCount { get; }
            public int ClassParent { get; }
            public int ClassRuntimeInfo { get; }
            public int ClassVTableSize { get; }
            public int ClassNextClassCache { get; }
            public int ClassDefFieldCount { get; }
            public int ClassDefNextClassCache { get; }
            public int ClassFieldName { get; }
            public int ClassFieldParent { get; }
            public int ClassFieldOffset { get; }
            public int ClassFieldType { get; }
            public int MonoTypeAttrs { get; }
            public int RuntimeInfoDomainVTables { get; }
            public int VTableData { get; }
            public int VTableVTable { get; }

            public const int GListData = 0x0;
            public const int GListNext = 0x8;
        }
    }
}
