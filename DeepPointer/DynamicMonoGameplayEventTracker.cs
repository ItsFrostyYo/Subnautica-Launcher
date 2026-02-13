using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Memory;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SubnauticaLauncher.Gameplay
{
    public sealed class DynamicMonoGameplayEventTracker
    {
        private const ushort FieldAttributeStatic = 0x10;
        private const int InitRetryMs = 3000;
        private const uint DONT_RESOLVE_DLL_REFERENCES = 0x00000001;
        private const int MaxCollectionItems = 4096;
        private const int MaxPlausibleTechType = 200000;
        private const int MaxPlausibleItemCount = 10000;
        private const int MaxKnownTechType = 10005;
        private const float MainMenuPosX = 0f;
        private const float MainMenuPosY = 1.75f;
        private const float MainMenuPosZ = 0f;
        private const float MainMenuPositionTolerance = 0.01f;

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
        private readonly string _gameName;

        private bool _ready;
        private bool _loggedInitFailure;
        private int _pid = -1;
        private DateTime _nextInitAttemptUtc = DateTime.MinValue;

        private MonoFlavor _flavor;
        private MonoLayout _layout = LayoutV1;
        private int _classFieldSize;

        private StaticFieldRef _knownTechField;
        private StaticFieldRef _scannerCompleteField;
        private StaticFieldRef _databankField;
        private StaticFieldRef _inventoryMainField;
        private StaticFieldRef _crafterMainField;
        private StaticFieldRef _playerMainField;
        private StaticFieldRef _uGuiMainField;
        private StaticFieldRef _uGuiMainMenuField;
        private StaticFieldRef _escapePodMainField;

        private bool _hasInventoryContainerOffset;
        private int _inventoryContainerOffset;
        private bool _hasItemsListOffset;
        private int _itemsListOffset;
        private readonly List<int> _itemsContainerCollectionOffsets = new();
        private bool _hasItemGroupItemsOffset;
        private int _itemGroupItemsOffset;
        private bool _hasItemGroupIdOffset;
        private int _itemGroupIdOffset;
        private bool _hasListSizeOffset;
        private int _listSizeOffset;
        private bool _useLegacyDictionaryLayout;
        private bool _hasDictionaryEntriesOffset;
        private int _dictionaryEntriesOffset;
        private bool _hasDictionaryVersionOffset;
        private int _dictionaryVersionOffset;
        private bool _hasLegacyDictionaryKeySlotsOffset;
        private int _legacyDictionaryKeySlotsOffset;
        private bool _hasLegacyDictionaryValueSlotsOffset;
        private int _legacyDictionaryValueSlotsOffset;
        private bool _hasLegacyDictionaryLinkSlotsOffset;
        private int _legacyDictionaryLinkSlotsOffset;
        private bool _hasLegacyDictionaryTouchedOffset;
        private int _legacyDictionaryTouchedOffset;
        private bool _hasUGuiLoadingOffset;
        private int _uGuiLoadingOffset;
        private bool _hasSceneLoadingIsLoadingOffset;
        private int _sceneLoadingIsLoadingOffset;
        private bool _hasInventoryItemOffset;
        private int _inventoryItemOffset;
        private bool _hasInventoryItemTechTypeOffset;
        private int _inventoryItemTechTypeOffset;
        private bool _hasPickupableTechTypeOffset;
        private int _pickupableTechTypeOffset;
        private bool _hasEscapePodIntroCinematicOffset;
        private int _escapePodIntroCinematicOffset;
        private bool _hasPlayerCinematicModeOffset;
        private int _playerCinematicModeOffset;
        private bool _hasCinematicModeActiveOffset;
        private int _cinematicModeActiveOffset;

        private readonly Dictionary<long, int?> _directTechTypeOffsetByClass = new();
        private readonly Dictionary<long, int?> _nestedObjectOffsetByClass = new();
        private readonly Dictionary<long, int?> _countFieldOffsetByClass = new();
        private readonly Dictionary<long, int?> _nestedListOffsetByClass = new();

        private bool _hasCrafterIsCraftingOffset;
        private int _crafterIsCraftingOffset;
        private bool _hasCrafterTechTypeOffset;
        private int _crafterTechTypeOffset;

        private bool _hasBaseline;
        private HashSet<int> _previousBlueprints = new();
        private HashSet<string> _previousDatabankEntries = new(StringComparer.Ordinal);
        private Dictionary<int, int> _previousInventoryCounts = new();
        private DateTime _lastInventoryParseWarnUtc = DateTime.MinValue;

        private bool _previousCrafting;
        private int _previousCraftTechType = -1;
        private DateTime _recentCraftEndedUtc = DateTime.MinValue;
        private int _recentCraftTechType = -1;
        private readonly DeepPointer? _legacyPosX;
        private readonly DeepPointer? _legacyPosY;
        private readonly DeepPointer? _legacyPosZ;
        private readonly DeepPointer? _modernPosX;
        private readonly DeepPointer? _modernPosY;
        private readonly DeepPointer? _modernPosZ;

        public DynamicMonoGameplayEventTracker(string gameName)
        {
            _gameName = gameName;
            _classFieldSize = Align(IntPtr.Size * 3 + 4, IntPtr.Size);

            if (string.Equals(_gameName, "Subnautica", StringComparison.OrdinalIgnoreCase))
            {
                _legacyPosX = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C0);
                _legacyPosY = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C4);
                _legacyPosZ = new DeepPointer("Subnautica.exe", 0x142B8C8, 0x180, 0x40, 0xA8, 0x7C8);

                _modernPosX = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA58);
                _modernPosY = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA5C);
                _modernPosZ = new DeepPointer("UnityPlayer.dll", 0x1839CE0, 0x28, 0x10, 0x150, 0xA60);
            }
        }

        public bool TryPoll(Process proc, out IReadOnlyList<GameplayEvent> events)
        {
            events = Array.Empty<GameplayEvent>();

            EnsureInitialized(proc);
            if (!_ready)
                return false;

            bool hasBlueprints = TryReadBlueprints(proc, out var currentBlueprints);
            bool hasDatabank = TryReadDatabankEntries(proc, out var currentDatabankEntries);
            bool hasInventory = TryReadInventoryCounts(proc, out var currentInventoryCounts);
            bool hasCraftState = TryReadCraftState(proc, out bool isCrafting, out int craftingTechType);

            if (!hasBlueprints && !hasDatabank && !hasInventory)
                return false;

            var now = DateTime.UtcNow;

            if (_previousCrafting && !isCrafting)
            {
                _recentCraftEndedUtc = now;
                _recentCraftTechType = _previousCraftTechType;
            }

            _previousCrafting = isCrafting;
            _previousCraftTechType = craftingTechType;

            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _previousBlueprints = currentBlueprints;
                _previousDatabankEntries = currentDatabankEntries;
                _previousInventoryCounts = currentInventoryCounts;
                return true;
            }

            var detectedEvents = new List<GameplayEvent>();

            if (hasBlueprints)
            {
                foreach (int techType in currentBlueprints)
                {
                    if (_previousBlueprints.Contains(techType))
                        continue;

                    detectedEvents.Add(CreateEvent(GameplayEventType.BlueprintUnlocked, techType.ToString(), 1, now));
                }

                _previousBlueprints = currentBlueprints;
            }

            if (hasDatabank)
            {
                foreach (string key in currentDatabankEntries)
                {
                    if (_previousDatabankEntries.Contains(key))
                        continue;

                    detectedEvents.Add(CreateEvent(GameplayEventType.DatabankEntryUnlocked, key, 1, now));
                }

                _previousDatabankEntries = currentDatabankEntries;
            }

            if (hasInventory)
            {
                foreach (int techType in currentInventoryCounts.Keys.Union(_previousInventoryCounts.Keys))
                {
                    int oldCount = _previousInventoryCounts.TryGetValue(techType, out int before) ? before : 0;
                    int newCount = currentInventoryCounts.TryGetValue(techType, out int after) ? after : 0;

                    int delta = newCount - oldCount;
                    if (delta == 0)
                        continue;

                    if (delta > 0)
                    {
                        GameplayEventType type = IsLikelyCraftedItem(techType, now, hasCraftState)
                            ? GameplayEventType.ItemCrafted
                            : GameplayEventType.ItemPickedUp;

                        detectedEvents.Add(CreateEvent(type, techType.ToString(), delta, now));
                    }
                    else
                    {
                        detectedEvents.Add(CreateEvent(GameplayEventType.ItemDropped, techType.ToString(), -delta, now));
                    }
                }

                _previousInventoryCounts = currentInventoryCounts;
            }

            events = detectedEvents;
            return true;
        }

        public bool TryDetectState(Process proc, out GameState state)
        {
            state = GameState.Unknown;

            EnsureInitialized(proc);
            if (!_ready)
                return false;

            return TryReadState(proc, out state);
        }

        private bool TryReadState(Process proc, out GameState state)
        {
            state = GameState.Unknown;
            bool hadAnySignal = false;
            bool hasPlayer = false;
            IntPtr playerMain = IntPtr.Zero;

            if (TryReadStaticObject(proc, _playerMainField, out playerMain))
            {
                hadAnySignal = true;
                hasPlayer = playerMain != IntPtr.Zero;
            }

            bool isLoading = false;
            if (TryReadStaticObject(proc, _uGuiMainField, out var uGuiMain) && uGuiMain != IntPtr.Zero)
            {
                hadAnySignal = true;

                if (_hasUGuiLoadingOffset &&
                    _hasSceneLoadingIsLoadingOffset &&
                    MemoryReader.ReadIntPtr(proc, IntPtr.Add(uGuiMain, _uGuiLoadingOffset), out var sceneLoading) &&
                    sceneLoading != IntPtr.Zero &&
                    TryReadBool(proc, IntPtr.Add(sceneLoading, _sceneLoadingIsLoadingOffset), out bool loadingFlag))
                {
                    hadAnySignal = true;
                    isLoading = loadingFlag;
                }
            }

            if (isLoading)
            {
                state = GameState.BlackScreen;
                return true;
            }

            bool isIntroCinematic = false;
            if (TryReadIntroCinematicActive(proc, out bool introCinematic))
            {
                hadAnySignal = true;
                isIntroCinematic = introCinematic;
            }

            if (hasPlayer &&
                _hasPlayerCinematicModeOffset &&
                TryReadBool(proc, IntPtr.Add(playerMain, _playerCinematicModeOffset), out bool playerCinematic))
            {
                hadAnySignal = true;
                isIntroCinematic |= playerCinematic;
            }

            if (TryReadIsMainMenuPosition(proc, out bool isMainMenuPosition))
            {
                hadAnySignal = true;
                if (isMainMenuPosition)
                {
                    state = GameState.MainMenu;
                    return true;
                }
            }

            if (hasPlayer)
            {
                if (isIntroCinematic)
                {
                    state = GameState.BlackScreen;
                    return true;
                }

                state = GameState.InGame;
                return true;
            }

            if (TryReadStaticObject(proc, _uGuiMainMenuField, out var mainMenu) && mainMenu != IntPtr.Zero)
            {
                hadAnySignal = true;
                state = isIntroCinematic ? GameState.BlackScreen : GameState.MainMenu;
                return true;
            }

            if (isIntroCinematic)
            {
                state = GameState.BlackScreen;
                return true;
            }

            return hadAnySignal;
        }

        private bool TryReadBool(Process proc, IntPtr address, out bool value)
        {
            value = false;

            if (MemoryReader.ReadByte(proc, address, out byte b))
            {
                value = b != 0;
                return true;
            }

            if (MemoryReader.ReadInt32(proc, address, out int i))
            {
                value = i != 0;
                return true;
            }

            return false;
        }

        private bool TryReadIntroCinematicActive(Process proc, out bool active)
        {
            active = false;

            if (!_escapePodMainField.IsValid || !_hasEscapePodIntroCinematicOffset || !_hasCinematicModeActiveOffset)
                return false;

            if (!TryReadStaticObject(proc, _escapePodMainField, out var escapePodMain) || escapePodMain == IntPtr.Zero)
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(escapePodMain, _escapePodIntroCinematicOffset), out var introObj) ||
                introObj == IntPtr.Zero)
            {
                return false;
            }

            return TryReadBool(proc, IntPtr.Add(introObj, _cinematicModeActiveOffset), out active);
        }

        private bool TryReadIsMainMenuPosition(Process proc, out bool isMainMenuPosition)
        {
            isMainMenuPosition = false;

            if (!TryReadPlayerPosition(proc, _modernPosX, _modernPosY, _modernPosZ, out float x, out float y, out float z) &&
                !TryReadPlayerPosition(proc, _legacyPosX, _legacyPosY, _legacyPosZ, out x, out y, out z))
            {
                return false;
            }

            isMainMenuPosition =
                Math.Abs(x - MainMenuPosX) <= MainMenuPositionTolerance &&
                Math.Abs(y - MainMenuPosY) <= MainMenuPositionTolerance &&
                Math.Abs(z - MainMenuPosZ) <= MainMenuPositionTolerance;
            return true;
        }

        private static bool TryReadPlayerPosition(
            Process proc,
            DeepPointer? xPtr,
            DeepPointer? yPtr,
            DeepPointer? zPtr,
            out float x,
            out float y,
            out float z)
        {
            x = 0f;
            y = 0f;
            z = 0f;

            if (xPtr == null || yPtr == null || zPtr == null)
                return false;

            return xPtr.TryReadFloat(proc, out x)
                && yPtr.TryReadFloat(proc, out y)
                && zPtr.TryReadFloat(proc, out z);
        }

        private GameplayEvent CreateEvent(GameplayEventType type, string key, int delta, DateTime now)
        {
            return new GameplayEvent
            {
                TimestampUtc = now,
                Game = _gameName,
                ProcessId = _pid,
                Type = type,
                Key = key,
                Delta = delta
            };
        }

        private bool IsLikelyCraftedItem(int techType, DateTime now, bool hasCraftState)
        {
            if (!hasCraftState)
                return false;

            if ((now - _recentCraftEndedUtc).TotalMilliseconds > 1800)
                return false;

            if (_recentCraftTechType < 0)
                return true;

            return _recentCraftTechType == techType;
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
                    return;

                if (TryInitialize(proc))
                {
                    _ready = true;
                    _loggedInitFailure = false;
                    Logger.Log(
                        $"Dynamic mono gameplay tracker initialized ({_gameName}, {_flavor}). " +
                        $"Sources: blueprints={_knownTechField.IsValid || _scannerCompleteField.IsValid}, " +
                        $"databank={_databankField.IsValid}, " +
                        $"inventory={_inventoryMainField.IsValid}, " +
                        $"craft={_crafterMainField.IsValid}, " +
                        $"dictMode={(_useLegacyDictionaryLayout ? "legacy" : "modern")}, " +
                        $"state={( _uGuiMainMenuField.IsValid || _uGuiMainField.IsValid || _playerMainField.IsValid)}");
                    return;
                }

                _nextInitAttemptUtc = DateTime.UtcNow.AddMilliseconds(InitRetryMs);
                if (!_loggedInitFailure)
                {
                    Logger.Warn($"Dynamic mono gameplay tracker init failed ({_gameName}).");
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

            _knownTechField = default;
            _scannerCompleteField = default;
            _databankField = default;
            _inventoryMainField = default;
            _crafterMainField = default;
            _playerMainField = default;
            _uGuiMainField = default;
            _uGuiMainMenuField = default;
            _escapePodMainField = default;

            _hasInventoryContainerOffset = false;
            _inventoryContainerOffset = 0;
            _hasItemsListOffset = false;
            _itemsListOffset = 0;
            _itemsContainerCollectionOffsets.Clear();
            _hasItemGroupItemsOffset = false;
            _itemGroupItemsOffset = 0;
            _hasItemGroupIdOffset = false;
            _itemGroupIdOffset = 0;
            _hasListSizeOffset = false;
            _listSizeOffset = 0;
            _useLegacyDictionaryLayout = false;
            _hasDictionaryEntriesOffset = false;
            _dictionaryEntriesOffset = 0;
            _hasDictionaryVersionOffset = false;
            _dictionaryVersionOffset = 0;
            _hasLegacyDictionaryKeySlotsOffset = false;
            _legacyDictionaryKeySlotsOffset = 0;
            _hasLegacyDictionaryValueSlotsOffset = false;
            _legacyDictionaryValueSlotsOffset = 0;
            _hasLegacyDictionaryLinkSlotsOffset = false;
            _legacyDictionaryLinkSlotsOffset = 0;
            _hasLegacyDictionaryTouchedOffset = false;
            _legacyDictionaryTouchedOffset = 0;
            _hasUGuiLoadingOffset = false;
            _uGuiLoadingOffset = 0;
            _hasSceneLoadingIsLoadingOffset = false;
            _sceneLoadingIsLoadingOffset = 0;
            _hasInventoryItemOffset = false;
            _inventoryItemOffset = 0;
            _hasInventoryItemTechTypeOffset = false;
            _inventoryItemTechTypeOffset = 0;
            _hasPickupableTechTypeOffset = false;
            _pickupableTechTypeOffset = 0;
            _hasEscapePodIntroCinematicOffset = false;
            _escapePodIntroCinematicOffset = 0;
            _hasPlayerCinematicModeOffset = false;
            _playerCinematicModeOffset = 0;
            _hasCinematicModeActiveOffset = false;
            _cinematicModeActiveOffset = 0;

            _directTechTypeOffsetByClass.Clear();
            _nestedObjectOffsetByClass.Clear();
            _countFieldOffsetByClass.Clear();
            _nestedListOffsetByClass.Clear();

            _hasCrafterIsCraftingOffset = false;
            _crafterIsCraftingOffset = 0;
            _hasCrafterTechTypeOffset = false;
            _crafterTechTypeOffset = 0;

            _hasBaseline = false;
            _previousBlueprints = new HashSet<int>();
            _previousDatabankEntries = new HashSet<string>(StringComparer.Ordinal);
            _previousInventoryCounts = new Dictionary<int, int>();
            _lastInventoryParseWarnUtc = DateTime.MinValue;
            _previousCrafting = false;
            _previousCraftTechType = -1;
            _recentCraftEndedUtc = DateTime.MinValue;
            _recentCraftTechType = -1;
        }

        private bool TryInitialize(Process proc)
        {
            if (!TryGetMonoModule(proc, out var monoModule, out var flavor) || monoModule == null)
                return false;

            _flavor = flavor;
            _layout = flavor == MonoFlavor.MonoV2 ? LayoutV2 : LayoutV1;

            IntPtr assemblyForeach = GetRemoteExportAddress(monoModule, "mono_assembly_foreach");
            if (assemblyForeach == IntPtr.Zero)
                return false;

            if (!TryFindAssembliesListPointer(proc, assemblyForeach, out IntPtr assembliesList))
                return false;

            IntPtr mainImage = FindAssemblyImage(proc, assembliesList, "Assembly-CSharp");
            if (mainImage == IntPtr.Zero)
                return false;

            IntPtr knownTechClass = FindClass(proc, mainImage, "KnownTech");
            IntPtr scannerClass = FindClass(proc, mainImage, "PDAScanner");
            IntPtr databankClass = FindClass(proc, mainImage, "PDAEncyclopedia");
            IntPtr inventoryClass = FindClass(proc, mainImage, "Inventory");
            IntPtr itemsContainerClass = FindClass(proc, mainImage, "ItemsContainer");
            IntPtr itemGroupClass = FindClass(proc, mainImage, "ItemGroup");
            IntPtr inventoryItemClass = FindClass(proc, mainImage, "InventoryItem");
            IntPtr pickupableClass = FindClass(proc, mainImage, "Pickupable");
            IntPtr playerClass = FindClass(proc, mainImage, "Player");
            IntPtr playerCinematicControllerClass = FindClass(proc, mainImage, "PlayerCinematicController");
            IntPtr escapePodClass = FindClass(proc, mainImage, "EscapePod");
            IntPtr uGuiClass = FindClass(proc, mainImage, "uGUI");
            IntPtr uGuiMainMenuClass = FindClass(proc, mainImage, "uGUI_MainMenu");
            IntPtr uGuiSceneLoadingClass = FindClass(proc, mainImage, "uGUI_SceneLoading");
            IntPtr crafterClass = FindClass(proc, mainImage, "CrafterLogic");
            IntPtr craftingMenuClass = FindClass(proc, mainImage, "uGUI_CraftingMenu");
            IntPtr coreImage = FindFirstAssemblyImage(proc, assembliesList,
                "mscorlib",
                "mscorlib.dll",
                "System.Private.CoreLib",
                "System.Private.CoreLib.dll",
                "netstandard",
                "netstandard.dll");

            bool anyReadableSource = false;

            if (knownTechClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, knownTechClass,
                new[] { "knownTech", "knownTechTypes", "known", "knownTechnology" }, out _knownTechField))
            {
                anyReadableSource = true;
            }

            if (scannerClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, scannerClass,
                new[] { "complete", "completed", "scans" }, out _scannerCompleteField))
            {
                anyReadableSource = true;
            }

            if (databankClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, databankClass,
                new[] { "knownEntries", "entries", "encyclopediaEntries", "unlockedEntries" }, out _databankField))
            {
                anyReadableSource = true;
            }

            if (inventoryClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, inventoryClass,
                new[] { "main", "_main", "s_main" }, out _inventoryMainField))
            {
                _hasInventoryContainerOffset = TryFindFieldOffsetAny(proc, inventoryClass,
                    new[] { "container", "_container", "m_container" }, out _inventoryContainerOffset);

                if (!_hasInventoryContainerOffset)
                {
                    _hasInventoryContainerOffset = TryFindFieldOffsetByContainsAny(proc, inventoryClass,
                        new[] { "container" }, out _inventoryContainerOffset);
                }

                anyReadableSource = true;
            }

            if (itemsContainerClass != IntPtr.Zero)
            {
                _hasItemsListOffset = TryFindFieldOffsetAny(proc, itemsContainerClass,
                    new[] { "items", "_items", "m_items" }, out _itemsListOffset);

                if (!_hasItemsListOffset)
                {
                    _hasItemsListOffset = TryFindFieldOffsetByContainsAny(proc, itemsContainerClass,
                        new[] { "items" }, out _itemsListOffset);
                }

                AddCollectionOffsetIfExists(proc, itemsContainerClass, "items", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "_items", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "m_items", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "itemsByType", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "_itemsByType", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "m_itemsByType", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "itemCounts", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "_itemCounts", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "m_itemCounts", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "techTypeCounts", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "_techTypeCounts", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "countByTechType", _itemsContainerCollectionOffsets);
                AddCollectionOffsetIfExists(proc, itemsContainerClass, "_countByTechType", _itemsContainerCollectionOffsets);
            }

            if (itemGroupClass != IntPtr.Zero)
            {
                _hasItemGroupItemsOffset = TryFindFieldOffsetAny(proc, itemGroupClass,
                    new[] { "items", "_items", "m_items" }, out _itemGroupItemsOffset);

                if (!_hasItemGroupItemsOffset)
                {
                    _hasItemGroupItemsOffset = TryFindFieldOffsetByContainsAny(proc, itemGroupClass,
                        new[] { "items" }, out _itemGroupItemsOffset);
                }

                _hasItemGroupIdOffset = TryFindFieldOffsetAny(proc, itemGroupClass,
                    new[] { "id", "_id", "m_id", "techType", "_techType", "m_techType" }, out _itemGroupIdOffset);

                if (!_hasItemGroupIdOffset)
                {
                    _hasItemGroupIdOffset = TryFindFieldOffsetByContainsAny(proc, itemGroupClass,
                        new[] { "id", "techType" }, out _itemGroupIdOffset);
                }
            }

            ResolveCoreCollectionOffsets(proc, coreImage);

            if (playerClass != IntPtr.Zero)
            {
                TryResolveStaticFieldRef(proc, playerClass,
                    new[] { "main", "_main", "s_main", "<main>k__BackingField" }, out _playerMainField);

                _hasPlayerCinematicModeOffset = TryFindFieldOffsetAny(proc, playerClass,
                    new[] { "_cinematicModeActive", "cinematicModeActive", "m_cinematicModeActive" },
                    out _playerCinematicModeOffset);
            }

            if (escapePodClass != IntPtr.Zero)
            {
                TryResolveStaticFieldRef(proc, escapePodClass,
                    new[] { "main", "_main", "s_main", "<main>k__BackingField" }, out _escapePodMainField);

                _hasEscapePodIntroCinematicOffset = TryFindFieldOffsetAny(proc, escapePodClass,
                    new[] { "introCinematic", "_introCinematic", "m_introCinematic" },
                    out _escapePodIntroCinematicOffset);
            }

            if (playerCinematicControllerClass != IntPtr.Zero)
            {
                _hasCinematicModeActiveOffset = TryFindFieldOffsetAny(proc, playerCinematicControllerClass,
                    new[] { "cinematicModeActive", "_cinematicModeActive", "m_cinematicModeActive" },
                    out _cinematicModeActiveOffset);
            }

            if (uGuiClass != IntPtr.Zero)
            {
                TryResolveStaticFieldRef(proc, uGuiClass,
                    new[] { "_main", "main", "s_main", "<main>k__BackingField" }, out _uGuiMainField);

                _hasUGuiLoadingOffset = TryFindFieldOffsetAny(proc, uGuiClass,
                    new[] { "loading", "_loading", "m_loading" }, out _uGuiLoadingOffset);

                if (!_hasUGuiLoadingOffset)
                {
                    _hasUGuiLoadingOffset = TryFindFieldOffsetByContainsAny(proc, uGuiClass,
                        new[] { "loading" }, out _uGuiLoadingOffset);
                }
            }

            if (uGuiMainMenuClass != IntPtr.Zero)
            {
                TryResolveStaticFieldRef(proc, uGuiMainMenuClass,
                    new[] { "main", "_main", "s_main", "<main>k__BackingField" }, out _uGuiMainMenuField);
            }

            if (uGuiSceneLoadingClass != IntPtr.Zero)
            {
                _hasSceneLoadingIsLoadingOffset = TryFindFieldOffsetAny(proc, uGuiSceneLoadingClass,
                    new[] { "isLoading", "_isLoading", "m_isLoading" }, out _sceneLoadingIsLoadingOffset);

                if (!_hasSceneLoadingIsLoadingOffset)
                {
                    _hasSceneLoadingIsLoadingOffset = TryFindFieldOffsetByContainsAny(proc, uGuiSceneLoadingClass,
                        new[] { "loading" }, out _sceneLoadingIsLoadingOffset);
                }
            }

            if (inventoryItemClass != IntPtr.Zero)
            {
                _hasInventoryItemOffset = TryFindFieldOffsetAny(proc, inventoryItemClass,
                    new[] { "item", "pickupable", "_item", "m_item", "_pickupable", "m_pickupable" }, out _inventoryItemOffset);

                _hasInventoryItemTechTypeOffset = TryFindFieldOffsetAny(proc, inventoryItemClass,
                    new[] { "techType", "_techType", "m_techType" }, out _inventoryItemTechTypeOffset);
            }

            if (pickupableClass != IntPtr.Zero)
            {
                _hasPickupableTechTypeOffset = TryFindFieldOffsetAny(proc, pickupableClass,
                    new[] { "techType", "_techType", "m_techType" }, out _pickupableTechTypeOffset);
            }

            if (crafterClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, crafterClass,
                new[] { "main" }, out _crafterMainField))
            {
                _hasCrafterIsCraftingOffset = TryFindFieldOffsetAny(proc, crafterClass,
                    new[] { "isCrafting", "crafting", "craftInProgress" }, out _crafterIsCraftingOffset);
                _hasCrafterTechTypeOffset = TryFindFieldOffsetAny(proc, crafterClass,
                    new[] { "craftingTechType", "techType", "currentTechType" }, out _crafterTechTypeOffset);
                anyReadableSource = true;
            }
            else if (craftingMenuClass != IntPtr.Zero && TryResolveStaticFieldRef(proc, craftingMenuClass,
                new[] { "main" }, out _crafterMainField))
            {
                _hasCrafterIsCraftingOffset = TryFindFieldOffsetAny(proc, craftingMenuClass,
                    new[] { "isCrafting", "crafting" }, out _crafterIsCraftingOffset);
                _hasCrafterTechTypeOffset = TryFindFieldOffsetAny(proc, craftingMenuClass,
                    new[] { "craftingTechType", "techType", "currentTechType" }, out _crafterTechTypeOffset);
                anyReadableSource = true;
            }

            bool hasStateSource = _playerMainField.IsValid
                || _uGuiMainField.IsValid
                || _uGuiMainMenuField.IsValid
                || _escapePodMainField.IsValid;
            return anyReadableSource || hasStateSource;
        }

        private void ResolveCoreCollectionOffsets(Process proc, IntPtr coreImage)
        {
            _hasListSizeOffset = false;
            _listSizeOffset = IntPtr.Size == 8 ? 0x18 : 0x0C;
            _useLegacyDictionaryLayout = _flavor == MonoFlavor.MonoV1;
            _hasDictionaryEntriesOffset = false;
            _dictionaryEntriesOffset = IntPtr.Size == 8 ? 0x18 : 0x10;
            _hasDictionaryVersionOffset = false;
            _dictionaryVersionOffset = 0;

            _hasLegacyDictionaryKeySlotsOffset = false;
            _legacyDictionaryKeySlotsOffset = 0;
            _hasLegacyDictionaryValueSlotsOffset = false;
            _legacyDictionaryValueSlotsOffset = 0;
            _hasLegacyDictionaryLinkSlotsOffset = false;
            _legacyDictionaryLinkSlotsOffset = 0;
            _hasLegacyDictionaryTouchedOffset = false;
            _legacyDictionaryTouchedOffset = 0;

            if (coreImage == IntPtr.Zero)
                return;

            IntPtr listClass = FindClass(proc, coreImage, "List`1");
            if (listClass != IntPtr.Zero)
            {
                _hasListSizeOffset = TryFindFieldOffsetAny(proc, listClass,
                    new[] { "_size", "size", "m_size", "_count", "count" }, out _listSizeOffset);
            }

            IntPtr dictClass = FindClass(proc, coreImage, "Dictionary`2");
            if (dictClass == IntPtr.Zero)
                return;

            _hasDictionaryEntriesOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "entries", "_entries", "m_entries" }, out _dictionaryEntriesOffset);

            _hasDictionaryVersionOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "version", "_version", "m_version" }, out _dictionaryVersionOffset);

            _hasLegacyDictionaryKeySlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "keySlots", "_keySlots", "m_keySlots" }, out _legacyDictionaryKeySlotsOffset);

            if (!_hasLegacyDictionaryKeySlotsOffset)
            {
                _hasLegacyDictionaryKeySlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                    new[] { "keyslots", "key" }, out _legacyDictionaryKeySlotsOffset);
            }

            _hasLegacyDictionaryValueSlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "valueSlots", "_valueSlots", "m_valueSlots" }, out _legacyDictionaryValueSlotsOffset);

            if (!_hasLegacyDictionaryValueSlotsOffset)
            {
                _hasLegacyDictionaryValueSlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                    new[] { "valueslots", "value" }, out _legacyDictionaryValueSlotsOffset);
            }

            _hasLegacyDictionaryLinkSlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "linkSlots", "_linkSlots", "m_linkSlots" }, out _legacyDictionaryLinkSlotsOffset);

            if (!_hasLegacyDictionaryLinkSlotsOffset)
            {
                _hasLegacyDictionaryLinkSlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                    new[] { "linkslots", "link" }, out _legacyDictionaryLinkSlotsOffset);
            }

            _hasLegacyDictionaryTouchedOffset = TryFindFieldOffsetAny(proc, dictClass,
                new[] { "touchedSlots", "_touchedSlots", "m_touchedSlots", "count", "_count", "m_count" },
                out _legacyDictionaryTouchedOffset);
        }

        private bool TryReadBlueprints(Process proc, out HashSet<int> values)
        {
            values = new HashSet<int>();
            bool found = false;

            if (TryReadStaticObject(proc, _knownTechField, out var knownTechObj))
            {
                if (TryReadKnownTechList(proc, knownTechObj, values))
                {
                    found = true;
                }
                else if (TryReadIntCollection(proc, knownTechObj, values))
                {
                    found = true;
                }
            }

            if (TryReadStaticObject(proc, _scannerCompleteField, out var scannerObj) &&
                TryReadIntCollection(proc, scannerObj, values))
            {
                found = true;
            }

            foreach (int tech in values.ToList())
            {
                if (tech <= 0 || tech > MaxKnownTechType)
                    values.Remove(tech);
            }

            return found;
        }

        private bool TryReadKnownTechList(Process proc, IntPtr knownTechObj, HashSet<int> values)
        {
            if (knownTechObj == IntPtr.Zero)
                return false;

            int slotsOffset = _flavor == MonoFlavor.MonoV1 ? 0x20 : 0x18;
            int countOffset = _flavor == MonoFlavor.MonoV1 ? 0x40 : 0x30;
            int slotStartOffset = 0x20;
            int slotStride = _flavor == MonoFlavor.MonoV1 ? 0x4 : 0xC;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(knownTechObj, slotsOffset), out var slots) ||
                slots == IntPtr.Zero ||
                !MemoryReader.ReadInt32(proc, IntPtr.Add(knownTechObj, countOffset), out int count))
            {
                return false;
            }

            if (count <= 0 || count > MaxCollectionItems)
                return false;

            int found = 0;
            for (int i = 0; i < count; i++)
            {
                IntPtr addr = IntPtr.Add(slots, slotStartOffset + i * slotStride);
                if (!MemoryReader.ReadInt32(proc, addr, out int tech))
                    break;

                if (tech <= 0 || tech > MaxKnownTechType)
                    continue;

                values.Add(tech);
                found++;
            }

            return found > 0;
        }

        private bool TryReadDatabankEntriesFromDictionary(Process proc, IntPtr dictObj, HashSet<string> values)
        {
            if (dictObj == IntPtr.Zero)
                return false;

            EnsureDictionaryOffsetsFromInstance(proc, dictObj);

            bool parsed = _useLegacyDictionaryLayout
                ? TryReadDatabankEntriesLegacy(proc, dictObj, values)
                : TryReadDatabankEntriesModern(proc, dictObj, values);

            if (!parsed && _hasDictionaryEntriesOffset)
            {
                // If legacy/modern guess was wrong for this build, try the other layout once.
                parsed = _useLegacyDictionaryLayout
                    ? TryReadDatabankEntriesModern(proc, dictObj, values)
                    : TryReadDatabankEntriesLegacy(proc, dictObj, values);
            }

            return parsed;
        }

        private bool TryReadDatabankEntriesModern(Process proc, IntPtr dictObj, HashSet<string> values)
        {
            if (!_hasDictionaryEntriesOffset)
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(dictObj, _dictionaryEntriesOffset), out var entriesArray) ||
                entriesArray == IntPtr.Zero ||
                !TryGetArrayLength(proc, entriesArray, out int length) ||
                length <= 0)
            {
                return false;
            }

            int stride = IntPtr.Size == 8 ? 24 : 16;
            int valueOffset = IntPtr.Size == 8 ? 0x10 : 0x0C;
            int dataOffset = GetMonoArrayDataOffset();
            int found = 0;

            for (int i = 0; i < length; i++)
            {
                IntPtr entry = IntPtr.Add(entriesArray, dataOffset + i * stride);
                if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                    break;

                if (hashCode < 0)
                    continue;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, 0x08), out var keyStringObj) ||
                    keyStringObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, valueOffset), out var entryData) ||
                    entryData == IntPtr.Zero)
                {
                    continue;
                }

                string key = ReadMonoString(proc, keyStringObj, 256);
                if (!IsPlausibleDatabankKey(key))
                    continue;

                values.Add(key);
                found++;
            }

            return found > 0;
        }

        private bool TryReadDatabankEntriesLegacy(Process proc, IntPtr dictObj, HashSet<string> values)
        {
            if (!_hasLegacyDictionaryValueSlotsOffset)
                return false;

            IntPtr keyArray = IntPtr.Zero;
            IntPtr valueArray = IntPtr.Zero;
            IntPtr linkArray = IntPtr.Zero;
            int touched = 0;

            if (_hasLegacyDictionaryKeySlotsOffset)
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(dictObj, _legacyDictionaryKeySlotsOffset), out keyArray);

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(dictObj, _legacyDictionaryValueSlotsOffset), out valueArray) ||
                valueArray == IntPtr.Zero)
            {
                return false;
            }

            if (_hasLegacyDictionaryLinkSlotsOffset)
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(dictObj, _legacyDictionaryLinkSlotsOffset), out linkArray);

            if (_hasLegacyDictionaryTouchedOffset)
                MemoryReader.ReadInt32(proc, IntPtr.Add(dictObj, _legacyDictionaryTouchedOffset), out touched);

            if (!TryGetArrayLength(proc, valueArray, out int valueLength) || valueLength <= 0)
                return false;

            int upper = valueLength;
            if (touched > 0 && touched <= valueLength)
                upper = touched;

            IntPtr keyBase = keyArray != IntPtr.Zero ? IntPtr.Add(keyArray, GetMonoArrayDataOffset()) : IntPtr.Zero;
            IntPtr valueBase = IntPtr.Add(valueArray, GetMonoArrayDataOffset());
            IntPtr linkBase = linkArray != IntPtr.Zero ? IntPtr.Add(linkArray, GetMonoArrayDataOffset()) : IntPtr.Zero;
            int ptrSize = IntPtr.Size;
            int found = 0;

            for (int i = 0; i < upper; i++)
            {
                if (linkBase != IntPtr.Zero)
                {
                    if (!MemoryReader.ReadInt32(proc, IntPtr.Add(linkBase, i * 8), out int h) || h == 0)
                        continue;
                }

                if (keyBase == IntPtr.Zero)
                    continue;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(keyBase, i * ptrSize), out var keyStringObj) ||
                    keyStringObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(valueBase, i * ptrSize), out var entryData) ||
                    entryData == IntPtr.Zero)
                {
                    continue;
                }

                string key = ReadMonoString(proc, keyStringObj, 256);
                if (!IsPlausibleDatabankKey(key))
                    continue;

                values.Add(key);
                found++;
            }

            return found > 0;
        }

        private bool TryReadDatabankEntries(Process proc, out HashSet<string> values)
        {
            values = new HashSet<string>(StringComparer.Ordinal);

            if (!TryReadStaticObject(proc, _databankField, out var databankObj))
                return false;

            bool found = false;

            if (TryReadDatabankEntriesFromDictionary(proc, databankObj, values))
                found = true;

            if (!found && TryReadStringCollection(proc, databankObj, values))
                found = true;

            return found;
        }

        private bool TryReadInventoryCounts(Process proc, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (TryReadInventoryCountsViaItemsDictionary(proc, out var dictCounts) && dictCounts.Count > 0)
            {
                counts = dictCounts;
            }
            else
            {
                bool parsed = TryReadInventoryCountsViaGenericCollections(proc, out var genericCounts);
                if (!parsed || genericCounts.Count == 0)
                {
                    MaybeLogInventoryParseWarning(parsed, genericCounts.Count);
                    return false;
                }

                counts = genericCounts;
            }

            foreach (int techType in counts.Keys.ToList())
            {
                if (!IsPlausibleTechType(techType) || counts[techType] <= 0)
                    counts.Remove(techType);
            }

            return counts.Count > 0;
        }

        private bool TryReadInventoryCountsViaItemsDictionary(Process proc, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (!_hasInventoryContainerOffset || !_hasItemsListOffset)
                return false;

            if (!TryReadStaticObject(proc, _inventoryMainField, out var inventoryMain) || inventoryMain == IntPtr.Zero)
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(inventoryMain, _inventoryContainerOffset), out var container) ||
                container == IntPtr.Zero)
            {
                return false;
            }

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(container, _itemsListOffset), out var dict) || dict == IntPtr.Zero)
                return false;

            EnsureDictionaryOffsetsFromInstance(proc, dict);

            bool parsed = _useLegacyDictionaryLayout
                ? TryReadLegacyItemsDictionaryCounts(proc, dict, counts)
                : TryReadModernItemsDictionaryCounts(proc, dict, counts);

            if (!parsed)
            {
                parsed = _useLegacyDictionaryLayout
                    ? TryReadModernItemsDictionaryCounts(proc, dict, counts)
                    : TryReadLegacyItemsDictionaryCounts(proc, dict, counts);
            }

            return parsed;
        }

        private bool TryReadModernItemsDictionaryCounts(Process proc, IntPtr dict, Dictionary<int, int> counts)
        {
            if (!_hasDictionaryEntriesOffset)
                return false;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                int versionBefore = 0;
                if (_hasDictionaryVersionOffset)
                    MemoryReader.ReadInt32(proc, IntPtr.Add(dict, _dictionaryVersionOffset), out versionBefore);

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(dict, _dictionaryEntriesOffset), out var entriesArray) ||
                    entriesArray == IntPtr.Zero ||
                    !TryGetArrayLength(proc, entriesArray, out int length) ||
                    length <= 0)
                {
                    break;
                }

                int stride = IntPtr.Size == 8 ? 24 : 16;
                int valueOffset = IntPtr.Size == 8 ? 0x10 : 0x0C;
                int dataOffset = GetMonoArrayDataOffset();

                counts.Clear();

                for (int i = 0; i < length; i++)
                {
                    IntPtr entry = IntPtr.Add(entriesArray, dataOffset + i * stride);

                    if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                        break;

                    if (hashCode < 0)
                        continue;

                    if (!MemoryReader.ReadInt32(proc, IntPtr.Add(entry, 0x08), out int keyInt) ||
                        !IsPlausibleTechType(keyInt))
                    {
                        continue;
                    }

                    if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, valueOffset), out var itemGroup) ||
                        itemGroup == IntPtr.Zero)
                    {
                        continue;
                    }

                    EnsureItemGroupOffsetsFromInstance(proc, itemGroup);
                    if (!_hasItemGroupItemsOffset)
                        continue;

                    int id = keyInt;
                    if (_hasItemGroupIdOffset &&
                        MemoryReader.ReadInt32(proc, IntPtr.Add(itemGroup, _itemGroupIdOffset), out int rawId) &&
                        IsPlausibleTechType(rawId))
                    {
                        id = rawId;
                    }

                    if (id != keyInt)
                        continue;

                    if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(itemGroup, _itemGroupItemsOffset), out var itemList) ||
                        itemList == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!ReadListCount(proc, itemList, out int itemCount))
                        continue;

                    if (!IsPlausibleItemCount(itemCount))
                        continue;

                    counts[keyInt] = itemCount;
                }

                int versionAfter = versionBefore;
                if (_hasDictionaryVersionOffset)
                    MemoryReader.ReadInt32(proc, IntPtr.Add(dict, _dictionaryVersionOffset), out versionAfter);

                if (!_hasDictionaryVersionOffset || versionAfter == versionBefore)
                    return counts.Count > 0;

                counts.Clear();
            }

            return counts.Count > 0;
        }

        private bool TryReadLegacyItemsDictionaryCounts(Process proc, IntPtr dict, Dictionary<int, int> counts)
        {
            if (!_hasLegacyDictionaryValueSlotsOffset)
                return false;

            IntPtr keyArray = IntPtr.Zero;
            IntPtr valueArray = IntPtr.Zero;
            IntPtr linkArray = IntPtr.Zero;
            int touched = 0;

            if (_hasLegacyDictionaryKeySlotsOffset)
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(dict, _legacyDictionaryKeySlotsOffset), out keyArray);

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(dict, _legacyDictionaryValueSlotsOffset), out valueArray) ||
                valueArray == IntPtr.Zero)
            {
                return false;
            }

            if (_hasLegacyDictionaryLinkSlotsOffset)
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(dict, _legacyDictionaryLinkSlotsOffset), out linkArray);

            if (_hasLegacyDictionaryTouchedOffset)
                MemoryReader.ReadInt32(proc, IntPtr.Add(dict, _legacyDictionaryTouchedOffset), out touched);

            if (!TryGetArrayLength(proc, valueArray, out int valueLength) || valueLength <= 0)
                return false;

            int upper = valueLength;
            if (touched > 0 && touched <= valueLength)
                upper = touched;

            IntPtr keyBase = keyArray != IntPtr.Zero ? IntPtr.Add(keyArray, GetMonoArrayDataOffset()) : IntPtr.Zero;
            IntPtr valueBase = IntPtr.Add(valueArray, GetMonoArrayDataOffset());
            IntPtr linkBase = linkArray != IntPtr.Zero ? IntPtr.Add(linkArray, GetMonoArrayDataOffset()) : IntPtr.Zero;
            int ptrSize = IntPtr.Size;

            for (int i = 0; i < upper; i++)
            {
                if (linkBase != IntPtr.Zero)
                {
                    // linkSlots entry = hashCode(int) + next(int)
                    if (!MemoryReader.ReadInt32(proc, IntPtr.Add(linkBase, i * 8), out int h) || h == 0)
                        continue;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(valueBase, i * ptrSize), out var itemGroup) ||
                    itemGroup == IntPtr.Zero)
                {
                    continue;
                }

                EnsureItemGroupOffsetsFromInstance(proc, itemGroup);
                if (!_hasItemGroupItemsOffset)
                    continue;

                int id = 0;
                if (_hasItemGroupIdOffset)
                    MemoryReader.ReadInt32(proc, IntPtr.Add(itemGroup, _itemGroupIdOffset), out id);

                int keyInt = id;
                if (keyBase != IntPtr.Zero &&
                    MemoryReader.ReadInt32(proc, IntPtr.Add(keyBase, i * 4), out int keySlot) &&
                    IsPlausibleTechType(keySlot))
                {
                    if (id == 0 || id == keySlot)
                        keyInt = keySlot;
                }

                if (!IsPlausibleTechType(keyInt))
                    continue;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(itemGroup, _itemGroupItemsOffset), out var itemList) ||
                    itemList == IntPtr.Zero)
                {
                    continue;
                }

                if (!ReadListCount(proc, itemList, out int itemCount) || !IsPlausibleItemCount(itemCount))
                    continue;

                counts[keyInt] = itemCount;
            }

            return counts.Count > 0;
        }

        private bool ReadListCount(Process proc, IntPtr listObj, out int count)
        {
            count = 0;

            if (listObj == IntPtr.Zero)
                return false;

            if (_hasListSizeOffset &&
                MemoryReader.ReadInt32(proc, IntPtr.Add(listObj, _listSizeOffset), out count) &&
                IsPlausibleItemCount(count))
            {
                return true;
            }

            return TryReadListHeader(proc, listObj, out _, out count) && IsPlausibleItemCount(count);
        }

        private void EnsureItemGroupOffsetsFromInstance(Process proc, IntPtr itemGroup)
        {
            if (itemGroup == IntPtr.Zero)
                return;

            if (!TryGetObjectClass(proc, itemGroup, out var klass) || klass == IntPtr.Zero)
                return;

            if (!_hasItemGroupItemsOffset)
            {
                _hasItemGroupItemsOffset = TryFindFieldOffsetAny(proc, klass,
                    new[] { "items", "_items", "m_items" }, out _itemGroupItemsOffset);

                if (!_hasItemGroupItemsOffset)
                {
                    _hasItemGroupItemsOffset = TryFindFieldOffsetByContainsAny(proc, klass,
                        new[] { "items" }, out _itemGroupItemsOffset);
                }
            }

            if (!_hasItemGroupIdOffset)
            {
                _hasItemGroupIdOffset = TryFindFieldOffsetAny(proc, klass,
                    new[] { "id", "_id", "m_id", "techType", "_techType", "m_techType" }, out _itemGroupIdOffset);

                if (!_hasItemGroupIdOffset)
                {
                    _hasItemGroupIdOffset = TryFindFieldOffsetByContainsAny(proc, klass,
                        new[] { "id", "techType" }, out _itemGroupIdOffset);
                }
            }
        }

        private void EnsureDictionaryOffsetsFromInstance(Process proc, IntPtr dictObj)
        {
            if (dictObj == IntPtr.Zero)
                return;

            if (!TryGetObjectClass(proc, dictObj, out var dictClass) || dictClass == IntPtr.Zero)
                return;

            if (!_hasDictionaryEntriesOffset)
            {
                _hasDictionaryEntriesOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "entries", "_entries", "m_entries" }, out _dictionaryEntriesOffset);
            }

            if (!_hasDictionaryVersionOffset)
            {
                _hasDictionaryVersionOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "version", "_version", "m_version" }, out _dictionaryVersionOffset);
            }

            if (!_hasLegacyDictionaryKeySlotsOffset)
            {
                _hasLegacyDictionaryKeySlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "keySlots", "_keySlots", "m_keySlots" }, out _legacyDictionaryKeySlotsOffset);

                if (!_hasLegacyDictionaryKeySlotsOffset)
                {
                    _hasLegacyDictionaryKeySlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                        new[] { "keyslots", "key" }, out _legacyDictionaryKeySlotsOffset);
                }
            }

            if (!_hasLegacyDictionaryValueSlotsOffset)
            {
                _hasLegacyDictionaryValueSlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "valueSlots", "_valueSlots", "m_valueSlots" }, out _legacyDictionaryValueSlotsOffset);

                if (!_hasLegacyDictionaryValueSlotsOffset)
                {
                    _hasLegacyDictionaryValueSlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                        new[] { "valueslots", "value" }, out _legacyDictionaryValueSlotsOffset);
                }
            }

            if (!_hasLegacyDictionaryLinkSlotsOffset)
            {
                _hasLegacyDictionaryLinkSlotsOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "linkSlots", "_linkSlots", "m_linkSlots" }, out _legacyDictionaryLinkSlotsOffset);

                if (!_hasLegacyDictionaryLinkSlotsOffset)
                {
                    _hasLegacyDictionaryLinkSlotsOffset = TryFindFieldOffsetByContainsAny(proc, dictClass,
                        new[] { "linkslots", "link" }, out _legacyDictionaryLinkSlotsOffset);
                }
            }

            if (!_hasLegacyDictionaryTouchedOffset)
            {
                _hasLegacyDictionaryTouchedOffset = TryFindFieldOffsetAny(proc, dictClass,
                    new[] { "touchedSlots", "_touchedSlots", "m_touchedSlots", "count", "_count", "m_count" },
                    out _legacyDictionaryTouchedOffset);
            }
        }

        private bool TryReadInventoryCountsViaGenericCollections(Process proc, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (!TryReadStaticObject(proc, _inventoryMainField, out var inventoryMain) || inventoryMain == IntPtr.Zero)
                return false;

            IntPtr container = inventoryMain;
            if (_hasInventoryContainerOffset &&
                (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(inventoryMain, _inventoryContainerOffset), out container) ||
                 container == IntPtr.Zero))
            {
                container = inventoryMain;
            }

            bool parsed = false;
            var seenCollections = new HashSet<long>();

            if (_hasItemsListOffset &&
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(container, _itemsListOffset), out var itemsList) &&
                itemsList != IntPtr.Zero)
            {
                parsed |= TryReadInventoryFromCollection(proc, itemsList, counts, seenCollections);
            }

            foreach (int offset in _itemsContainerCollectionOffsets)
            {
                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(container, offset), out var collectionObj) ||
                    collectionObj == IntPtr.Zero)
                {
                    continue;
                }

                parsed |= TryReadInventoryFromCollection(proc, collectionObj, counts, seenCollections);
            }

            if (!parsed)
            {
                // Fallback: some builds expose item collections directly on inventory.
                parsed = TryReadInventoryFromCollection(proc, container, counts, seenCollections);
            }

            return parsed;
        }

        private void MaybeLogInventoryParseWarning(bool parsedAnyCollection, int countEntries)
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _lastInventoryParseWarnUtc).TotalSeconds < 8)
                return;

            _lastInventoryParseWarnUtc = now;

            Logger.Warn(
                $"[GameEvent] Inventory parse empty ({_gameName}). " +
                $"parsed={parsedAnyCollection}, entries={countEntries}, " +
                $"hasContainerOffset={_hasInventoryContainerOffset}, " +
                $"collectionOffsets={_itemsContainerCollectionOffsets.Count}, " +
                $"dictMode={(_useLegacyDictionaryLayout ? "legacy" : "modern")}, " +
                $"hasItemsDictOffset={_hasItemsListOffset}, " +
                $"hasItemGroupItemsOffset={_hasItemGroupItemsOffset}, " +
                $"hasInventoryItemOffset={_hasInventoryItemOffset}, " +
                $"hasPickupableTechTypeOffset={_hasPickupableTechTypeOffset}");
        }

        private bool TryReadInventoryFromCollection(
            Process proc,
            IntPtr collectionObj,
            Dictionary<int, int> destination,
            HashSet<long> seenCollections)
        {
            if (collectionObj == IntPtr.Zero)
                return false;

            if (!seenCollections.Add(collectionObj.ToInt64()))
                return false;

            Dictionary<int, int>? best = null;
            int bestScore = int.MinValue;
            bool parsed = false;

            if (TryReadListObjectPointers(proc, collectionObj, out var pointers) &&
                TryBuildCountsFromPointers(proc, pointers, out var pointerCounts))
            {
                ConsiderInventoryCandidate(pointerCounts, ref best, ref bestScore);
                parsed = true;
            }

            if (TryReadDictionaryIntValueCounts(proc, collectionObj, out var intValueCounts))
            {
                ConsiderInventoryCandidate(intValueCounts, ref best, ref bestScore);
                parsed = true;
            }

            if (TryReadDictionaryIntKeyObjectCounts(proc, collectionObj, out var intKeyObjectCounts))
            {
                ConsiderInventoryCandidate(intKeyObjectCounts, ref best, ref bestScore);
                parsed = true;
            }

            if (TryReadDictionaryObjectKeyCounts(proc, collectionObj, out var objectKeyCounts))
            {
                ConsiderInventoryCandidate(objectKeyCounts, ref best, ref bestScore);
                parsed = true;
            }

            if (!parsed || best == null || best.Count == 0)
                return false;

            MergeCountsUsingMax(destination, best);
            return true;
        }

        private static void ConsiderInventoryCandidate(
            IReadOnlyDictionary<int, int> candidate,
            ref Dictionary<int, int>? best,
            ref int bestScore)
        {
            if (candidate.Count == 0)
                return;

            int unique = candidate.Count;
            int total = 0;
            foreach (int value in candidate.Values)
            {
                if (value > 0)
                    total += value;
            }

            // Prefer candidates with real stack counts, then with more unique tech types.
            int score = total * 8 + unique;
            if (score <= bestScore)
                return;

            best = new Dictionary<int, int>(candidate);
            bestScore = score;
        }

        private bool TryBuildCountsFromPointers(Process proc, IReadOnlyList<IntPtr> pointers, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            int inspected = 0;
            int resolved = 0;

            foreach (IntPtr pointer in pointers)
            {
                if (pointer == IntPtr.Zero)
                    continue;

                inspected++;
                if (!TryResolveTechTypeFromObject(proc, pointer, out int techType))
                    continue;

                counts[techType] = counts.TryGetValue(techType, out int current)
                    ? current + 1
                    : 1;

                resolved++;
            }

            if (resolved == 0)
                return false;

            // Filter out mis-parsed collections where barely any entries resolved.
            if (inspected > 8 && resolved * 4 < inspected)
                return false;

            return true;
        }

        private bool TryReadDictionaryIntValueCounts(Process proc, IntPtr dictObj, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (!TryReadDictionaryEntriesArray(proc, dictObj, out IntPtr entriesArray, out int count) ||
                !TryGetArrayLength(proc, entriesArray, out int length))
            {
                return false;
            }

            int entries = Math.Min(Math.Min(length, count <= 0 ? length : count + 32), MaxCollectionItems);
            if (entries <= 0)
                return false;

            int[] strideCandidates = { 16, 20, 24, 28, 32, 40, 48 };
            int[] keyOffsets = { 8, 12, 16, 20, 24, 28 };
            int[] valueOffsets = { 12, 16, 20, 24, 28, 32 };

            int bestScore = 0;
            Dictionary<int, int>? best = null;

            foreach (int stride in strideCandidates)
            {
                foreach (int keyOffset in keyOffsets)
                {
                    foreach (int valueOffset in valueOffsets)
                    {
                        if (keyOffset == valueOffset)
                            continue;

                        var temp = new Dictionary<int, int>();
                        int score = 0;

                        for (int i = 0; i < entries; i++)
                        {
                            IntPtr entry = IntPtr.Add(entriesArray, GetMonoArrayDataOffset() + i * stride);

                            if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                                break;

                            if (hashCode < 0)
                                continue;

                            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(entry, keyOffset), out int techType) ||
                                !IsPlausibleTechType(techType))
                            {
                                continue;
                            }

                            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(entry, valueOffset), out int amount) ||
                                !IsPlausibleItemCount(amount))
                            {
                                continue;
                            }

                            temp[techType] = temp.TryGetValue(techType, out int current)
                                ? Math.Max(current, amount)
                                : amount;

                            score++;
                        }

                        if (score > bestScore && temp.Count > 0)
                        {
                            bestScore = score;
                            best = temp;
                        }
                    }
                }
            }

            if (best == null)
                return false;

            counts = best;
            return true;
        }

        private bool TryReadDictionaryIntKeyObjectCounts(Process proc, IntPtr dictObj, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (!TryReadDictionaryEntriesArray(proc, dictObj, out IntPtr entriesArray, out int count) ||
                !TryGetArrayLength(proc, entriesArray, out int length))
            {
                return false;
            }

            int entries = Math.Min(Math.Min(length, count <= 0 ? length : count + 32), MaxCollectionItems);
            if (entries <= 0)
                return false;

            int[] strideCandidates = { 20, 24, 28, 32, 40, 48 };
            int[] keyOffsets = { 8, 12, 16, 20, 24 };
            int[] valuePointerOffsets = { 12, 16, 20, 24, 28, 32 };

            int bestScore = 0;
            Dictionary<int, int>? best = null;

            foreach (int stride in strideCandidates)
            {
                foreach (int keyOffset in keyOffsets)
                {
                    foreach (int valuePointerOffset in valuePointerOffsets)
                    {
                        if (keyOffset == valuePointerOffset)
                            continue;

                        var temp = new Dictionary<int, int>();
                        int score = 0;

                        for (int i = 0; i < entries; i++)
                        {
                            IntPtr entry = IntPtr.Add(entriesArray, GetMonoArrayDataOffset() + i * stride);

                            if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                                break;

                            if (hashCode < 0)
                                continue;

                            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(entry, keyOffset), out int techType) ||
                                !IsPlausibleTechType(techType))
                            {
                                continue;
                            }

                            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, valuePointerOffset), out var valueObj) ||
                                valueObj == IntPtr.Zero)
                            {
                                continue;
                            }

                            int amount = 1;
                            if (TryReadCountFromObject(proc, valueObj, out int objectAmount))
                                amount = objectAmount;

                            temp[techType] = temp.TryGetValue(techType, out int current)
                                ? Math.Max(current, amount)
                                : amount;

                            score++;
                        }

                        if (score > bestScore && temp.Count > 0)
                        {
                            bestScore = score;
                            best = temp;
                        }
                    }
                }
            }

            if (best == null)
                return false;

            counts = best;
            return true;
        }

        private bool TryReadDictionaryObjectKeyCounts(Process proc, IntPtr dictObj, out Dictionary<int, int> counts)
        {
            counts = new Dictionary<int, int>();

            if (!TryReadDictionaryEntriesArray(proc, dictObj, out IntPtr entriesArray, out int count) ||
                !TryGetArrayLength(proc, entriesArray, out int length))
            {
                return false;
            }

            int entries = Math.Min(Math.Min(length, count <= 0 ? length : count + 32), MaxCollectionItems);
            if (entries <= 0)
                return false;

            int[] strideCandidates = { 20, 24, 28, 32, 40, 48 };
            int[] pointerOffsets = { 8, 12, 16, 20, 24, 28, 32 };
            int[] valueOffsets = { 12, 16, 20, 24, 28, 32 };

            int bestScore = 0;
            Dictionary<int, int>? best = null;

            foreach (int stride in strideCandidates)
            {
                foreach (int keyPointerOffset in pointerOffsets)
                {
                    foreach (int valueOffset in valueOffsets)
                    {
                        if (keyPointerOffset == valueOffset)
                            continue;

                        var temp = new Dictionary<int, int>();
                        int score = 0;

                        for (int i = 0; i < entries; i++)
                        {
                            IntPtr entry = IntPtr.Add(entriesArray, GetMonoArrayDataOffset() + i * stride);

                            if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                                break;

                            if (hashCode < 0)
                                continue;

                            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, keyPointerOffset), out var keyObj) ||
                                keyObj == IntPtr.Zero)
                            {
                                continue;
                            }

                            if (!TryResolveTechTypeFromObject(proc, keyObj, out int techType))
                                continue;

                            int amount = 1;
                            if (MemoryReader.ReadInt32(proc, IntPtr.Add(entry, valueOffset), out int valueAsInt) &&
                                IsPlausibleItemCount(valueAsInt))
                            {
                                amount = valueAsInt;
                            }

                            temp[techType] = temp.TryGetValue(techType, out int current)
                                ? Math.Max(current, amount)
                                : amount;

                            score++;
                        }

                        if (score > bestScore && temp.Count > 0)
                        {
                            bestScore = score;
                            best = temp;
                        }
                    }
                }
            }

            if (best == null)
                return false;

            counts = best;
            return true;
        }

        private bool TryResolveTechTypeFromObject(Process proc, IntPtr objectPtr, out int techType)
        {
            return TryResolveTechTypeFromObject(proc, objectPtr, 0, out techType);
        }

        private bool TryResolveTechTypeFromObject(Process proc, IntPtr objectPtr, int depth, out int techType)
        {
            techType = -1;

            if (objectPtr == IntPtr.Zero)
                return false;

            if (depth > 4)
                return false;

            if (TryReadTechTypeAtOffset(proc, objectPtr, _hasPickupableTechTypeOffset, _pickupableTechTypeOffset, out techType))
                return true;

            if (TryReadTechTypeAtOffset(proc, objectPtr, _hasInventoryItemTechTypeOffset, _inventoryItemTechTypeOffset, out techType))
                return true;

            if (_hasInventoryItemOffset &&
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(objectPtr, _inventoryItemOffset), out var nestedItem) &&
                nestedItem != IntPtr.Zero &&
                TryReadTechTypeAtOffset(proc, nestedItem, _hasPickupableTechTypeOffset, _pickupableTechTypeOffset, out techType))
            {
                return true;
            }

            if (!TryGetObjectClass(proc, objectPtr, out var klass) || klass == IntPtr.Zero)
                return false;

            if (TryGetOrCacheFieldOffset(proc, klass, _directTechTypeOffsetByClass,
                new[] { "techType", "_techType", "m_techType" }, out int directOffset) &&
                MemoryReader.ReadInt32(proc, IntPtr.Add(objectPtr, directOffset), out int directTechType) &&
                IsPlausibleTechType(directTechType))
            {
                techType = directTechType;
                return true;
            }

            if (TryGetOrCacheFieldOffset(proc, klass, _nestedObjectOffsetByClass,
                new[] { "item", "_item", "m_item", "pickupable", "_pickupable", "m_pickupable" }, out int nestedOffset) &&
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(objectPtr, nestedOffset), out var nestedObject) &&
                nestedObject != IntPtr.Zero &&
                nestedObject != objectPtr)
            {
                return TryResolveTechTypeFromObject(proc, nestedObject, depth + 1, out techType);
            }

            return false;
        }

        private bool TryReadCountFromObject(Process proc, IntPtr objectPtr, out int amount)
        {
            amount = 0;

            if (objectPtr == IntPtr.Zero)
                return false;

            if (!TryGetObjectClass(proc, objectPtr, out var klass) || klass == IntPtr.Zero)
                return false;

            if (TryGetOrCacheFieldOffset(proc, klass, _countFieldOffsetByClass,
                new[]
                {
                    "count", "_count", "m_count",
                    "amount", "_amount", "m_amount",
                    "quantity", "_quantity", "m_quantity",
                    "size", "_size", "m_size",
                    "itemCount", "_itemCount", "m_itemCount"
                }, out int countOffset) &&
                MemoryReader.ReadInt32(proc, IntPtr.Add(objectPtr, countOffset), out int countValue) &&
                IsPlausibleItemCount(countValue))
            {
                amount = countValue;
                return true;
            }

            // Sometimes the object itself is List<T>/HashSet<T> wrapper.
            if (TryReadListHeader(proc, objectPtr, out _, out int listSize) &&
                IsPlausibleItemCount(listSize))
            {
                amount = listSize;
                return true;
            }

            // Some wrappers expose the real collection through an items/list field.
            if (TryGetOrCacheFieldOffset(proc, klass, _nestedListOffsetByClass,
                new[]
                {
                    "items", "_items", "m_items",
                    "list", "_list", "m_list",
                    "entries", "_entries", "m_entries"
                }, out int listOffset) &&
                MemoryReader.ReadIntPtr(proc, IntPtr.Add(objectPtr, listOffset), out var listObj) &&
                listObj != IntPtr.Zero)
            {
                if (TryReadListHeader(proc, listObj, out _, out int nestedListSize) &&
                    IsPlausibleItemCount(nestedListSize))
                {
                    amount = nestedListSize;
                    return true;
                }

                if (TryReadDictionaryEntriesArray(proc, listObj, out _, out int dictCount) &&
                    IsPlausibleItemCount(dictCount))
                {
                    amount = dictCount;
                    return true;
                }
            }

            return false;
        }

        private bool TryReadTechTypeAtOffset(Process proc, IntPtr objectPtr, bool hasOffset, int offset, out int techType)
        {
            techType = -1;

            if (!hasOffset)
                return false;

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(objectPtr, offset), out int value) ||
                !IsPlausibleTechType(value))
            {
                return false;
            }

            techType = value;
            return true;
        }

        private bool TryGetOrCacheFieldOffset(
            Process proc,
            IntPtr klass,
            Dictionary<long, int?> cache,
            string[] names,
            out int offset)
        {
            long key = klass.ToInt64();

            if (cache.TryGetValue(key, out int? cached))
            {
                if (cached.HasValue)
                {
                    offset = cached.Value;
                    return true;
                }

                offset = 0;
                return false;
            }

            if (TryFindFieldOffsetAny(proc, klass, names, out offset))
            {
                cache[key] = offset;
                return true;
            }

            cache[key] = null;
            offset = 0;
            return false;
        }

        private static void MergeCountsUsingMax(Dictionary<int, int> destination, IReadOnlyDictionary<int, int> source)
        {
            foreach (var kv in source)
            {
                if (kv.Value <= 0)
                    continue;

                if (!destination.TryGetValue(kv.Key, out int current) || kv.Value > current)
                    destination[kv.Key] = kv.Value;
            }
        }

        private static bool IsPlausibleTechType(int value)
        {
            return value > 0 && value <= MaxPlausibleTechType;
        }

        private static bool IsPlausibleItemCount(int value)
        {
            return value > 0 && value <= MaxPlausibleItemCount;
        }

        private static bool IsPlausibleDatabankKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.Length > 128)
                return false;

            foreach (char c in key)
            {
                if (char.IsLetterOrDigit(c))
                    continue;

                if (c == '_' || c == '-' || c == '.' || c == ':' || c == '/')
                    continue;

                return false;
            }

            return true;
        }

        private bool TryReadCraftState(Process proc, out bool isCrafting, out int craftingTechType)
        {
            isCrafting = false;
            craftingTechType = -1;

            if (!TryReadStaticObject(proc, _crafterMainField, out var crafterMain) || crafterMain == IntPtr.Zero)
                return false;

            bool found = false;

            if (_hasCrafterIsCraftingOffset)
            {
                if (MemoryReader.ReadByte(proc, IntPtr.Add(crafterMain, _crafterIsCraftingOffset), out byte b))
                {
                    isCrafting = b != 0;
                    found = true;
                }
                else if (MemoryReader.ReadInt32(proc, IntPtr.Add(crafterMain, _crafterIsCraftingOffset), out int iv))
                {
                    isCrafting = iv != 0;
                    found = true;
                }
            }

            if (_hasCrafterTechTypeOffset &&
                MemoryReader.ReadInt32(proc, IntPtr.Add(crafterMain, _crafterTechTypeOffset), out int techType))
            {
                craftingTechType = techType;
                found = true;
            }

            return found;
        }

        private bool TryReadStaticObject(Process proc, StaticFieldRef fieldRef, out IntPtr value)
        {
            value = IntPtr.Zero;
            if (!fieldRef.IsValid)
                return false;

            return MemoryReader.ReadIntPtr(proc, IntPtr.Add(fieldRef.StaticBase, fieldRef.FieldOffset), out value);
        }

        private bool TryResolveStaticFieldRef(Process proc, IntPtr klass, string[] fieldNames, out StaticFieldRef fieldRef)
        {
            fieldRef = default;

            foreach (string fieldName in fieldNames)
            {
                if (!TryFindStaticField(proc, klass, fieldName, out int staticOffset, out IntPtr parentClass))
                    continue;

                if (!TryGetStaticDataAddress(proc, parentClass, out IntPtr staticDataBase))
                    continue;

                fieldRef = new StaticFieldRef(staticDataBase, staticOffset);
                return true;
            }

            return false;
        }

        private bool TryFindFieldOffsetAny(Process proc, IntPtr klass, string[] fieldNames, out int fieldOffset)
        {
            foreach (string fieldName in fieldNames)
            {
                if (TryFindFieldOffset(proc, klass, fieldName, out fieldOffset))
                    return true;
            }

            fieldOffset = 0;
            return false;
        }

        private bool TryFindFieldOffsetByContainsAny(Process proc, IntPtr klass, string[] fragments, out int fieldOffset)
        {
            var normalized = fragments
                .Select(NormalizeFieldName)
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            return TryFindFieldOffsetByPredicate(proc, klass, fieldName =>
            {
                string f = NormalizeFieldName(fieldName);
                if (string.IsNullOrWhiteSpace(f))
                    return false;

                return normalized.Any(n => f.Contains(n, StringComparison.OrdinalIgnoreCase));
            }, out fieldOffset);
        }

        private bool TryFindFieldOffsetByPredicate(Process proc, IntPtr klass, Func<string, bool> predicate, out int fieldOffset)
        {
            fieldOffset = 0;

            IntPtr current = klass;
            int parentGuard = 0;
            while (current != IntPtr.Zero && parentGuard++ < 32)
            {
                if (TryFindFieldByPredicate(proc, current, predicate, out fieldOffset))
                    return true;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(current, _layout.ClassParent), out current))
                    break;
            }

            return false;
        }

        private bool TryFindFieldByPredicate(Process proc, IntPtr klass, Func<string, bool> predicate, out int fieldOffset)
        {
            fieldOffset = 0;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, _layout.ClassFields), out var fields) || fields == IntPtr.Zero)
                return false;

            int countOffset = _flavor == MonoFlavor.MonoV2
                ? _layout.ClassDefFieldCount
                : _layout.ClassFieldCount;

            if (countOffset < 0 || !MemoryReader.ReadInt32(proc, IntPtr.Add(klass, countOffset), out int fieldCount))
                return false;

            if (fieldCount <= 0 || fieldCount > 4000)
                return false;

            for (int i = 0; i < fieldCount; i++)
            {
                IntPtr field = new(fields.ToInt64() + (long)i * _classFieldSize);

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(field, _layout.ClassFieldName), out var fieldNamePtr))
                    continue;

                string currentFieldName = MemoryReader.ReadUtf8String(proc, fieldNamePtr, 128);
                if (!predicate(currentFieldName))
                    continue;

                if (!MemoryReader.ReadInt32(proc, IntPtr.Add(field, _layout.ClassFieldOffset), out fieldOffset))
                    return false;

                return true;
            }

            return false;
        }

        private void AddCollectionOffsetIfExists(Process proc, IntPtr klass, string fieldName, List<int> offsets)
        {
            if (!TryFindFieldOffset(proc, klass, fieldName, out int offset))
                return;

            if (!offsets.Contains(offset))
                offsets.Add(offset);
        }

        private bool TryReadIntCollection(Process proc, IntPtr objectPtr, HashSet<int> values)
        {
            if (objectPtr == IntPtr.Zero)
                return false;

            bool parsed = false;

            parsed |= TryReadListInts(proc, objectPtr, values);
            parsed |= TryReadHashSetInts(proc, objectPtr, values);
            parsed |= TryReadDictionaryIntKeys(proc, objectPtr, values);

            return parsed;
        }

        private bool TryReadStringCollection(Process proc, IntPtr objectPtr, HashSet<string> values)
        {
            if (objectPtr == IntPtr.Zero)
                return false;

            bool parsed = false;

            parsed |= TryReadListStrings(proc, objectPtr, values);
            parsed |= TryReadHashSetStrings(proc, objectPtr, values);
            parsed |= TryReadDictionaryStringKeys(proc, objectPtr, values);

            return parsed;
        }

        private bool TryReadListInts(Process proc, IntPtr listObj, HashSet<int> values)
        {
            if (!TryReadListHeader(proc, listObj, out IntPtr itemsArray, out int size))
                return false;

            if (size <= 0)
                return true;

            int arrayData = GetMonoArrayDataOffset();
            int count = Math.Min(size, MaxCollectionItems);
            int plausible = 0;
            var temp = new List<int>(count);

            for (int i = 0; i < count; i++)
            {
                if (!MemoryReader.ReadInt32(proc, IntPtr.Add(itemsArray, arrayData + i * 4), out int value))
                    break;

                temp.Add(value);
                if (value >= 0 && value <= 100000)
                    plausible++;
            }

            if (temp.Count == 0 || plausible < temp.Count / 2)
                return false;

            foreach (int value in temp)
                values.Add(value);

            return true;
        }

        private bool TryReadListStrings(Process proc, IntPtr listObj, HashSet<string> values)
        {
            if (!TryReadListHeader(proc, listObj, out IntPtr itemsArray, out int size))
                return false;

            if (size <= 0)
                return true;

            int arrayData = GetMonoArrayDataOffset();
            int count = Math.Min(size, MaxCollectionItems);
            int valid = 0;

            for (int i = 0; i < count; i++)
            {
                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(itemsArray, arrayData + i * IntPtr.Size), out var stringObj) ||
                    stringObj == IntPtr.Zero)
                {
                    continue;
                }

                string text = ReadMonoString(proc, stringObj, 256);
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                values.Add(text);
                valid++;
            }

            return valid > 0;
        }

        private bool TryReadListObjectPointers(Process proc, IntPtr listObj, out List<IntPtr> pointers)
        {
            pointers = new List<IntPtr>();

            if (!TryReadListHeader(proc, listObj, out IntPtr itemsArray, out int size))
                return false;

            if (size <= 0)
                return true;

            int arrayData = GetMonoArrayDataOffset();
            int count = Math.Min(size, MaxCollectionItems);

            for (int i = 0; i < count; i++)
            {
                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(itemsArray, arrayData + i * IntPtr.Size), out var value))
                    break;

                pointers.Add(value);
            }

            return true;
        }

        private bool TryReadListHeader(Process proc, IntPtr listObj, out IntPtr itemsArray, out int size)
        {
            itemsArray = IntPtr.Zero;
            size = 0;

            if (!TryGetObjectClass(proc, listObj, out var listClass) || listClass == IntPtr.Zero)
                return false;

            if (!TryFindFieldOffsetAny(proc, listClass, new[] { "_items", "items" }, out int itemsOffset))
                return false;

            if (!TryFindFieldOffsetAny(proc, listClass, new[] { "_size", "size", "_count", "count" }, out int sizeOffset))
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(listObj, itemsOffset), out itemsArray) || itemsArray == IntPtr.Zero)
                return false;

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(listObj, sizeOffset), out size))
                return false;

            if (size < 0 || size > MaxCollectionItems)
                return false;

            return true;
        }

        private bool TryReadHashSetInts(Process proc, IntPtr setObj, HashSet<int> values)
        {
            if (!TryReadHashSetSlotArray(proc, setObj, out IntPtr slotsArray, out int count))
                return false;

            return TryReadIntFromStructArray(proc, slotsArray, count, values);
        }

        private bool TryReadHashSetStrings(Process proc, IntPtr setObj, HashSet<string> values)
        {
            if (!TryReadHashSetSlotArray(proc, setObj, out IntPtr slotsArray, out int count))
                return false;

            return TryReadStringFromStructArray(proc, slotsArray, count, values);
        }

        private bool TryReadHashSetSlotArray(Process proc, IntPtr setObj, out IntPtr slotsArray, out int count)
        {
            slotsArray = IntPtr.Zero;
            count = 0;

            if (!TryGetObjectClass(proc, setObj, out var setClass) || setClass == IntPtr.Zero)
                return false;

            if (!TryFindFieldOffsetAny(proc, setClass, new[] { "_slots", "slots" }, out int slotsOffset))
                return false;

            if (!TryFindFieldOffsetAny(proc, setClass, new[] { "_count", "count" }, out int countOffset))
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(setObj, slotsOffset), out slotsArray) || slotsArray == IntPtr.Zero)
                return false;

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(setObj, countOffset), out count))
                return false;

            if (count < 0 || count > MaxCollectionItems)
                return false;

            return true;
        }

        private bool TryReadDictionaryIntKeys(Process proc, IntPtr dictObj, HashSet<int> values)
        {
            if (!TryReadDictionaryEntriesArray(proc, dictObj, out IntPtr entriesArray, out int count))
                return false;

            return TryReadIntFromStructArray(proc, entriesArray, count, values);
        }

        private bool TryReadDictionaryStringKeys(Process proc, IntPtr dictObj, HashSet<string> values)
        {
            if (!TryReadDictionaryEntriesArray(proc, dictObj, out IntPtr entriesArray, out int count))
                return false;

            return TryReadStringFromStructArray(proc, entriesArray, count, values);
        }

        private bool TryReadDictionaryEntriesArray(Process proc, IntPtr dictObj, out IntPtr entriesArray, out int count)
        {
            entriesArray = IntPtr.Zero;
            count = 0;

            if (!TryGetObjectClass(proc, dictObj, out var dictClass) || dictClass == IntPtr.Zero)
                return false;

            if (!TryFindFieldOffsetAny(proc, dictClass, new[] { "_entries", "entries" }, out int entriesOffset))
                return false;

            if (!TryFindFieldOffsetAny(proc, dictClass, new[] { "_count", "count" }, out int countOffset))
                return false;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(dictObj, entriesOffset), out entriesArray) || entriesArray == IntPtr.Zero)
                return false;

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(dictObj, countOffset), out count))
                return false;

            if (count < 0 || count > MaxCollectionItems)
                return false;

            return true;
        }

        private bool TryReadIntFromStructArray(Process proc, IntPtr arrayObj, int count, HashSet<int> values)
        {
            if (!TryGetArrayLength(proc, arrayObj, out int length))
                return false;

            int entries = Math.Min(Math.Min(length, count <= 0 ? length : count + 32), MaxCollectionItems);
            if (entries <= 0)
                return true;

            int bestScore = 0;
            HashSet<int>? best = null;

            int[] strideCandidates = { 12, 16, 20, 24, 28, 32, 40 };
            int[] valueOffsetCandidates = { 8, 12, 16, 20 };

            foreach (int stride in strideCandidates)
            {
                foreach (int valueOffset in valueOffsetCandidates)
                {
                    var temp = new HashSet<int>();
                    int score = 0;

                    for (int i = 0; i < entries; i++)
                    {
                        IntPtr entry = IntPtr.Add(arrayObj, GetMonoArrayDataOffset() + i * stride);

                        if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                            break;

                        if (hashCode < 0)
                            continue;

                        if (!MemoryReader.ReadInt32(proc, IntPtr.Add(entry, valueOffset), out int value))
                            continue;

                        temp.Add(value);
                        score++;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = temp;
                    }
                }
            }

            if (best == null || bestScore == 0)
                return false;

            foreach (int value in best)
                values.Add(value);

            return true;
        }

        private bool TryReadStringFromStructArray(Process proc, IntPtr arrayObj, int count, HashSet<string> values)
        {
            if (!TryGetArrayLength(proc, arrayObj, out int length))
                return false;

            int entries = Math.Min(Math.Min(length, count <= 0 ? length : count + 32), MaxCollectionItems);
            if (entries <= 0)
                return true;

            int bestScore = 0;
            HashSet<string>? best = null;

            int[] strideCandidates = { 16, 20, 24, 28, 32, 40, 48 };
            int[] pointerOffsetCandidates = { 8, 12, 16, 20, 24 };

            foreach (int stride in strideCandidates)
            {
                foreach (int pointerOffset in pointerOffsetCandidates)
                {
                    var temp = new HashSet<string>(StringComparer.Ordinal);
                    int score = 0;

                    for (int i = 0; i < entries; i++)
                    {
                        IntPtr entry = IntPtr.Add(arrayObj, GetMonoArrayDataOffset() + i * stride);

                        if (!MemoryReader.ReadInt32(proc, entry, out int hashCode))
                            break;

                        if (hashCode < 0)
                            continue;

                        if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(entry, pointerOffset), out var stringObj) ||
                            stringObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        string value = ReadMonoString(proc, stringObj, 256);
                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        temp.Add(value);
                        score++;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = temp;
                    }
                }
            }

            if (best == null || bestScore == 0)
                return false;

            foreach (string value in best)
                values.Add(value);

            return true;
        }

        private static int GetMonoArrayLengthOffset()
        {
            return IntPtr.Size == 8 ? 0x18 : 0x0C;
        }

        private static int GetMonoArrayDataOffset()
        {
            return IntPtr.Size == 8 ? 0x20 : 0x10;
        }

        private static string ReadMonoString(Process proc, IntPtr stringObj, int maxChars)
        {
            if (stringObj == IntPtr.Zero)
                return string.Empty;

            int lengthOffset = IntPtr.Size == 8 ? 0x10 : 0x08;
            int charsOffset = IntPtr.Size == 8 ? 0x14 : 0x0C;

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(stringObj, lengthOffset), out int charCount))
                return string.Empty;

            if (charCount <= 0)
                return string.Empty;

            if (charCount > maxChars)
                charCount = maxChars;

            if (!MemoryReader.ReadBytes(proc, IntPtr.Add(stringObj, charsOffset), charCount * 2, out var bytes))
                return string.Empty;

            string value;
            try
            {
                value = Encoding.Unicode.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            foreach (char c in value)
            {
                if (char.IsControl(c) && c != '\t')
                    return string.Empty;
            }

            return value.Trim();
        }

        private bool TryGetArrayLength(Process proc, IntPtr arrayObj, out int length)
        {
            return MemoryReader.ReadInt32(proc, IntPtr.Add(arrayObj, GetMonoArrayLengthOffset()), out length)
                && length >= 0
                && length <= MaxCollectionItems;
        }

        private bool TryGetObjectClass(Process proc, IntPtr objectPtr, out IntPtr klass)
        {
            klass = IntPtr.Zero;

            if (!MemoryReader.ReadIntPtr(proc, objectPtr, out var vtable) || vtable == IntPtr.Zero)
                return false;

            return MemoryReader.ReadIntPtr(proc, vtable, out klass) && klass != IntPtr.Zero;
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
                return IntPtr.Zero;

            try
            {
                IntPtr localExport = GetProcAddress(localModule, exportName);
                if (localExport == IntPtr.Zero)
                    return IntPtr.Zero;

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
                return false;

            int operandOffset = IntPtr.Size == 8
                ? FindPattern(bytes, new byte[] { 0x48, 0x8B, 0x0D })
                : FindPattern(bytes, new byte[] { 0xFF, 0x35 });

            if (operandOffset < 0)
                return false;

            IntPtr operandAddress = new(
                monoAssemblyForeach.ToInt64() + operandOffset + (IntPtr.Size == 8 ? 3 : 2));

            IntPtr globalAssembliesAddress;
            if (IntPtr.Size == 8)
            {
                if (!MemoryReader.ReadInt32(proc, operandAddress, out int rel))
                    return false;

                globalAssembliesAddress = new IntPtr(operandAddress.ToInt64() + 4 + rel);
            }
            else
            {
                if (!MemoryReader.ReadInt32(proc, operandAddress, out int abs))
                    return false;

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
                    break;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(assembly, _layout.AssemblyImage), out var image) || image == IntPtr.Zero)
                    break;

                if (MemoryReader.ReadIntPtr(proc, IntPtr.Add(image, _layout.ImageAssemblyName), out var imageNamePtr))
                {
                    string current = MemoryReader.ReadUtf8String(proc, imageNamePtr, 128);
                    if (current.Equals(imageName, StringComparison.OrdinalIgnoreCase))
                        return image;
                }

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(node, MonoLayout.GListNext), out node))
                    break;
            }

            return IntPtr.Zero;
        }

        private IntPtr FindFirstAssemblyImage(Process proc, IntPtr assembliesList, params string[] imageNames)
        {
            foreach (string imageName in imageNames)
            {
                IntPtr image = FindAssemblyImage(proc, assembliesList, imageName);
                if (image != IntPtr.Zero)
                    return image;
            }

            return IntPtr.Zero;
        }

        private IntPtr FindClass(Process proc, IntPtr image, string className)
        {
            IntPtr classCache = IntPtr.Add(image, _layout.ImageClassCache);

            if (!MemoryReader.ReadInt32(proc, IntPtr.Add(classCache, _layout.HashSize), out int size))
                return IntPtr.Zero;

            if (size <= 0 || size > 200000)
                return IntPtr.Zero;

            if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(classCache, _layout.HashTable), out var table) || table == IntPtr.Zero)
                return IntPtr.Zero;

            int pointerSize = IntPtr.Size;

            for (int i = 0; i < size; i++)
            {
                IntPtr bucketEntryAddress = new IntPtr(table.ToInt64() + (long)i * pointerSize);
                if (!MemoryReader.ReadIntPtr(proc, bucketEntryAddress, out var klass))
                    continue;

                int chainGuard = 0;
                while (klass != IntPtr.Zero && chainGuard++ < 10000)
                {
                    if (MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, _layout.ClassName), out var classNamePtr))
                    {
                        string currentName = MemoryReader.ReadUtf8String(proc, classNamePtr, 128);
                        if (currentName.Equals(className, StringComparison.Ordinal))
                            return klass;
                    }

                    int nextOffset = _flavor == MonoFlavor.MonoV2
                        ? _layout.ClassDefNextClassCache
                        : _layout.ClassNextClassCache;

                    if (nextOffset < 0 || !MemoryReader.ReadIntPtr(proc, IntPtr.Add(klass, nextOffset), out klass))
                        break;
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
                    return true;

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(current, _layout.ClassParent), out current))
                    break;
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
                return false;

            if (!MemoryReader.ReadUInt16(proc, IntPtr.Add(typePtr, _layout.MonoTypeAttrs), out ushort attrs))
                return false;

            if ((attrs & FieldAttributeStatic) == 0)
                return false;

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
                return false;

            int countOffset = _flavor == MonoFlavor.MonoV2
                ? _layout.ClassDefFieldCount
                : _layout.ClassFieldCount;

            if (countOffset < 0 || !MemoryReader.ReadInt32(proc, IntPtr.Add(klass, countOffset), out int fieldCount))
                return false;

            if (fieldCount <= 0 || fieldCount > 4000)
                return false;

            for (int i = 0; i < fieldCount; i++)
            {
                IntPtr field = new(fields.ToInt64() + (long)i * _classFieldSize);

                if (!MemoryReader.ReadIntPtr(proc, IntPtr.Add(field, _layout.ClassFieldName), out var fieldNamePtr))
                    continue;

                string currentFieldName = MemoryReader.ReadUtf8String(proc, fieldNamePtr, 128);
                if (!FieldNameMatches(currentFieldName, fieldName))
                    continue;

                if (!MemoryReader.ReadInt32(proc, IntPtr.Add(field, _layout.ClassFieldOffset), out fieldOffset))
                    return false;

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
                    return false;

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

        private static bool FieldNameMatches(string actual, string expected)
        {
            if (actual.Equals(expected, StringComparison.Ordinal))
                return true;

            if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return true;

            string normalizedActual = NormalizeFieldName(actual);
            string normalizedExpected = NormalizeFieldName(expected);

            return normalizedActual.Equals(normalizedExpected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFieldName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            value = value.Trim();

            if (value.StartsWith("m_", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(2);
            else if (value.StartsWith("_", StringComparison.Ordinal))
                value = value.Substring(1);

            return value.Replace("_", string.Empty);
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
                    return i;
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

        private readonly struct StaticFieldRef
        {
            public StaticFieldRef(IntPtr staticBase, int fieldOffset)
            {
                StaticBase = staticBase;
                FieldOffset = fieldOffset;
            }

            public IntPtr StaticBase { get; }
            public int FieldOffset { get; }
            public bool IsValid => StaticBase != IntPtr.Zero;
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
