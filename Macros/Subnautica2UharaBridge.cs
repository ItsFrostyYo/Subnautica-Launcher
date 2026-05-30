using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Installer;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Runtime.Loader;
using System.Threading;

namespace SubnauticaLauncher.Macros;

[SupportedOSPlatform("windows")]
internal sealed class Subnautica2UharaBridge
{
    private const string LiveSplitCoreResourceName = "SubnauticaLauncher.Tools.LiveSplit.Core.dll";
    private const string LiveSplitViewResourceName = "SubnauticaLauncher.Tools.LiveSplit.View.dll";
    private const string SharpDisasmResourceName = "SubnauticaLauncher.Tools.SharpDisasm.dll";
    private const string Uhara10ResourceName = "SubnauticaLauncher.Tools.uhara10";

    internal static class Watchers
    {
        public const string MainMenuTick = nameof(MainMenuTick);
        public const string MainLobbyConstruct = nameof(MainLobbyConstruct);
        public const string PlaySinglePlayerClicked = nameof(PlaySinglePlayerClicked);
        public const string NewGameClicked = nameof(NewGameClicked);
        public const string CreativeModeSelected = nameof(CreativeModeSelected);
        public const string ConfirmStartClicked = nameof(ConfirmStartClicked);
        public const string BuildNumberVisible = nameof(BuildNumberVisible);
        public const string CharacterSelectReady = nameof(CharacterSelectReady);
        public const string CharacterSelectButton1Clicked = nameof(CharacterSelectButton1Clicked);
        public const string CharacterSelectButton2Clicked = nameof(CharacterSelectButton2Clicked);
        public const string CharacterSelectButtonDefaultClicked = nameof(CharacterSelectButtonDefaultClicked);
        public const string CharacterSelectButton3Clicked = nameof(CharacterSelectButton3Clicked);
        public const string CharacterConfirmReady = nameof(CharacterConfirmReady);
        public const string CharacterConfirmReadyButton1 = nameof(CharacterConfirmReadyButton1);
        public const string CharacterConfirmReadyButton2 = nameof(CharacterConfirmReadyButton2);
        public const string CharacterConfirmReadyButton3A = nameof(CharacterConfirmReadyButton3A);
        public const string CharacterConfirmReadyButton3B = nameof(CharacterConfirmReadyButton3B);
        public const string CharacterConfirmClicked5 = nameof(CharacterConfirmClicked5);
        public const string CharacterConfirmClicked6 = nameof(CharacterConfirmClicked6);
        public const string CharacterConfirmClicked7 = nameof(CharacterConfirmClicked7);
    }

    private readonly object _sync = new();

    private Assembly? _uharaAssembly;
    private Type? _mainType;
    private Type? _eventsType;
    private Type? _ptrResolverType;
    private MethodInfo? _setProcessMethod;
    private MethodInfo? _mainUpdateMethod;
    private MethodInfo? _checkFlagMethod;
    private MethodInfo? _functionFlagNamedMethod;
    private FieldInfo? _varsField;
    private FieldInfo? _currentField;
    private FieldInfo? _oldField;
    private FieldInfo? _memoryWatchersField;
    private FieldInfo? _stringWatchersField;
    private FieldInfo? _countableWatchersField;
    private object? _mainInstance;
    private object? _eventsInstance;
    private object? _resolverInstance;
    private int _attachedProcessId;
    private bool _resolverHooked;

    public static Subnautica2UharaBridge Shared { get; } = new();

    private static readonly (string Name, string ClassName, string ObjectName, string FunctionName)[] WatchDefinitions =
    [
        (
            Watchers.MainMenuTick,
            "WBP_MainMenuButton_C",
            "PlaySinglePlayerButton",
            "Tick"
        ),
        (
            Watchers.MainLobbyConstruct,
            "WBP_MainLobbyScreen_C",
            "WBP_MainLobbyScreen_C",
            "Construct"
        ),
        (
            Watchers.PlaySinglePlayerClicked,
            "WBP_MainLobbyScreen_C",
            "WBP_MainLobbyScreen_C",
            "BndEvt__WBP_MainLobbyScreen_PlaySinglePlayerButton_K2Node_ComponentBoundEvent_4_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.NewGameClicked,
            "WBP_MainLobbyScreen_C",
            "WBP_MainLobbyScreen_C",
            "BndEvt__WBP_MainLobbyScreen_WBP_LoadGamePanel_K2Node_ComponentBoundEvent_0_OnNewGameClicked__DelegateSignature"
        ),
        (
            Watchers.CreativeModeSelected,
            "WBP_GameModeButton_C",
            "CreativeModeButton",
            "BP_OnSelected"
        ),
        (
            Watchers.ConfirmStartClicked,
            "WBP_CreateGameScreen_C",
            "WBP_CreateGameScreen",
            "BndEvt__WBP_CreateGameScreen_StartButton_K2Node_ComponentBoundEvent_5_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.BuildNumberVisible,
            "WBP_BuildNumber_C",
            "WBP_BuildNumber",
            "UpdatePrecacheText"
        ),
        (
            Watchers.CharacterSelectReady,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "AreButtonsEnabled"
        ),
        (
            Watchers.CharacterSelectButton1Clicked,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_WBP_CharacterSelectButton_1_K2Node_ComponentBoundEvent_1_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterSelectButton2Clicked,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_WBP_CharacterSelectButton_2_K2Node_ComponentBoundEvent_2_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterSelectButtonDefaultClicked,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_WBP_CharacterSelectButton_K2Node_ComponentBoundEvent_0_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterSelectButton3Clicked,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_WBP_CharacterSelectButton_3_K2Node_ComponentBoundEvent_3_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterConfirmReady,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "Finished_30B316914BE08C9AA90C6A8A07582CD0"
        ),
        (
            Watchers.CharacterConfirmReadyButton1,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "Finished_4A9539F947442723AD4774BBFD67B038"
        ),
        (
            Watchers.CharacterConfirmReadyButton2,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "Finished_6393888A49A80E93DF2A49959FE48FD2"
        ),
        (
            Watchers.CharacterConfirmReadyButton3A,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "Finished_9EE709F2470DBBB027E7A1B2236D1F7C"
        ),
        (
            Watchers.CharacterConfirmReadyButton3B,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "Finished_EAEC21384587751992FC78BD9311272F"
        ),
        (
            Watchers.CharacterConfirmClicked5,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_Play_K2Node_ComponentBoundEvent_5_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterConfirmClicked6,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_Play_1_K2Node_ComponentBoundEvent_6_CommonButtonBaseClicked__DelegateSignature"
        ),
        (
            Watchers.CharacterConfirmClicked7,
            "WBP_CharacterSelectScreen_C",
            "WBP_CharacterSelectScreen_C",
            "BndEvt__WBP_CharacterSelectScreen_Play_K2Node_ComponentBoundEvent_7_CommonButtonBaseClicked__DelegateSignature"
        )
    ];

    public void Refresh(Process process)
    {
        lock (_sync)
        {
            EnsureInitializedLocked(process);
            _setProcessMethod!.Invoke(_mainInstance, [process]);
            _mainUpdateMethod!.Invoke(_mainInstance, null);
        }
    }

    public ulong GetCounter(string watcherName)
    {
        lock (_sync)
        {
            return GetCounterLocked(watcherName);
        }
    }

    public bool CheckFlag(string watcherName)
    {
        lock (_sync)
        {
            return (bool)(_checkFlagMethod!.Invoke(_resolverInstance, [watcherName]) ?? false);
        }
    }

    public GameState DetectState(Process process, int sampleDurationMs = 250)
    {
        lock (_sync)
        {
            EnsureInitializedLocked(process);
            _setProcessMethod!.Invoke(_mainInstance, [process]);
            _mainUpdateMethod!.Invoke(_mainInstance, null);
            ulong tickBaseline = GetCounterLocked(Watchers.MainMenuTick);
            ulong constructBaseline = GetCounterLocked(Watchers.MainLobbyConstruct);

            Thread.Sleep(Math.Max(50, sampleDurationMs));

            _setProcessMethod!.Invoke(_mainInstance, [process]);
            _mainUpdateMethod!.Invoke(_mainInstance, null);
            ulong tick = GetCounterLocked(Watchers.MainMenuTick);
            ulong construct = GetCounterLocked(Watchers.MainLobbyConstruct);

            return tick > tickBaseline || construct > constructBaseline
                ? GameState.MainMenu
                : GameState.InGame;
        }
    }

    private void EnsureInitializedLocked(Process process)
    {
        if (_mainInstance != null && _eventsInstance != null && _attachedProcessId == process.Id)
            return;

        LoadAssembliesLocked();
        ResetStaticWatcherStateLocked();
        SeedMainStaticsLocked();

        _mainInstance = Activator.CreateInstance(_mainType!)
            ?? throw new InvalidOperationException("Failed to create uhara Main instance.");

        _setProcessMethod!.Invoke(_mainInstance, [process]);

        _eventsInstance = Activator.CreateInstance(_eventsType!)
            ?? throw new InvalidOperationException("Failed to create uhara Events instance.");

        _resolverInstance = Activator.CreateInstance(_ptrResolverType!)
            ?? throw new InvalidOperationException("Failed to create uhara PtrResolver instance.");

        RegisterWatchersLocked();
        _attachedProcessId = process.Id;
    }

    private void LoadAssembliesLocked()
    {
        if (_uharaAssembly != null)
            return;

        EnsurePayloadFilesExist();
        EnsureAssemblyResolverLocked();

        LoadEmbeddedAssemblyIfMissing("LiveSplit.Core");
        LoadEmbeddedAssemblyIfMissing("LiveSplit.View");
        LoadEmbeddedAssemblyIfMissing("SharpDisasm");

        _uharaAssembly = LoadBytesAssemblyIfMissing(AppPaths.Uhara10Path, "uhara10");
        _mainType = _uharaAssembly.GetType("Main", throwOnError: true)
            ?? throw new InvalidOperationException("uhara Main type was not found.");
        _eventsType = _uharaAssembly.GetType("Tools+UnrealEngine+Default+Events", throwOnError: true)
            ?? throw new InvalidOperationException("uhara UnrealEngine Events type was not found.");
        _ptrResolverType = _uharaAssembly.GetType("PtrResolver", throwOnError: true)
            ?? throw new InvalidOperationException("uhara PtrResolver type was not found.");

        BindingFlags staticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        BindingFlags instancePublic = BindingFlags.Instance | BindingFlags.Public;

        _varsField = _mainType.GetField("Vars", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main.Vars field was not found.");
        _currentField = _mainType.GetField("_current", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main._current field was not found.");
        _oldField = _mainType.GetField("_old", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main._old field was not found.");
        _memoryWatchersField = _mainType.GetField("MemoryWatchers", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main.MemoryWatchers field was not found.");
        _stringWatchersField = _mainType.GetField("StringWatchers", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main.StringWatchers field was not found.");
        _countableWatchersField = _mainType.GetField("CountableWatchers", staticNonPublic)
            ?? throw new InvalidOperationException("uhara Main.CountableWatchers field was not found.");

        _setProcessMethod = _mainType.GetMethod("SetProcess", instancePublic)
            ?? throw new InvalidOperationException("uhara Main.SetProcess method was not found.");
        _mainUpdateMethod = _mainType.GetMethod("Update", instancePublic)
            ?? throw new InvalidOperationException("uhara Main.Update method was not found.");
        _checkFlagMethod = _ptrResolverType.GetMethod("CheckFlag", instancePublic)
            ?? throw new InvalidOperationException("uhara PtrResolver.CheckFlag method was not found.");
        _functionFlagNamedMethod = _eventsType.GetMethod(
            "FunctionFlag",
            instancePublic,
            null,
            [typeof(string), typeof(string), typeof(string), typeof(string)],
            null)
            ?? throw new InvalidOperationException("uhara Events.FunctionFlag watcher overload was not found.");
    }

    private void SeedMainStaticsLocked()
    {
        _varsField!.SetValue(null, new ExpandoObject());
        _currentField!.SetValue(null, new Dictionary<string, object>(StringComparer.Ordinal));
        _oldField!.SetValue(null, new Dictionary<string, object>(StringComparer.Ordinal));
    }

    private void ResetStaticWatcherStateLocked()
    {
        ClearListField(_memoryWatchersField);
        ClearListField(_stringWatchersField);
        ClearListField(_countableWatchersField);
        _mainInstance = null;
        _eventsInstance = null;
        _resolverInstance = null;
        _attachedProcessId = 0;
    }

    private void ClearListField(FieldInfo? field)
    {
        if (field?.GetValue(null) is IList list)
            list.Clear();
    }

    private void RegisterWatchersLocked()
    {
        foreach ((string name, string className, string objectName, string functionName) in WatchDefinitions)
        {
            _functionFlagNamedMethod!.Invoke(
                _eventsInstance,
                [name, className, objectName, functionName]);
        }
    }

    private ulong GetCounterLocked(string watcherName)
    {
        if (_currentField?.GetValue(null) is not IDictionary<string, object> current ||
            !current.TryGetValue(watcherName, out object? raw) ||
            raw == null)
        {
            return 0;
        }

        try
        {
            return Convert.ToUInt64(raw);
        }
        catch
        {
            return 0;
        }
    }

    private static Assembly LoadEmbeddedAssemblyIfMissing(string simpleName)
    {
        Assembly? loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        if (loaded != null)
            return loaded;

        string resourceName = GetEmbeddedAssemblyResourceName(simpleName);
        byte[] bytes = ReadEmbeddedResourceBytes(resourceName);
        return Assembly.Load(bytes);
    }

    private static Assembly LoadBytesAssemblyIfMissing(string path, string simpleName)
    {
        Assembly? loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase));

        if (loaded != null)
            return loaded;

        return Assembly.Load(File.ReadAllBytes(path));
    }

    private void EnsureAssemblyResolverLocked()
    {
        if (_resolverHooked)
            return;

        AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledAssembly;
        AssemblyLoadContext.Default.Resolving += ResolveBundledAssembly;
        _resolverHooked = true;
    }

    private static Assembly? ResolveBundledAssembly(object? sender, ResolveEventArgs args)
    {
        return ResolveBundledAssembly(new AssemblyName(args.Name));
    }

    private static Assembly? ResolveBundledAssembly(AssemblyLoadContext _, AssemblyName assemblyName)
    {
        return ResolveBundledAssembly(assemblyName);
    }

    private static Assembly? ResolveBundledAssembly(AssemblyName assemblyName)
    {
        string? simpleName = assemblyName.Name switch
        {
            "LiveSplit.Core" => "LiveSplit.Core",
            "LiveSplit.View" => "LiveSplit.View",
            "SharpDisasm" => "SharpDisasm",
            _ => null
        };

        if (string.IsNullOrWhiteSpace(simpleName))
            return null;

        Assembly? alreadyLoaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));

        if (alreadyLoaded != null)
            return alreadyLoaded;

        return LoadEmbeddedAssemblyIfMissing(simpleName);
    }

    private static void EnsurePayloadFilesExist()
    {
        Subnautica2ToolInstaller.EnsureInstalled();

        string[] required = [AppPaths.Uhara10Path];

        string[] missing = required
            .Where(path => !File.Exists(path))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new FileNotFoundException(
                "Missing required Subnautica 2 memory reader payload files: " +
                string.Join(", ", missing));
        }

        string[] missingResources =
        new[]
        {
            Uhara10ResourceName,
            LiveSplitCoreResourceName,
            LiveSplitViewResourceName,
            SharpDisasmResourceName
        }
        .Where(resourceName => typeof(Subnautica2UharaBridge).Assembly.GetManifestResourceStream(resourceName) == null)
        .ToArray();

        if (missingResources.Length > 0)
        {
            throw new FileNotFoundException(
                "Missing embedded Subnautica 2 memory reader dependency resources: " +
                string.Join(", ", missingResources));
        }
    }

    private static string GetEmbeddedAssemblyResourceName(string simpleName)
    {
        return simpleName switch
        {
            "LiveSplit.Core" => LiveSplitCoreResourceName,
            "LiveSplit.View" => LiveSplitViewResourceName,
            "SharpDisasm" => SharpDisasmResourceName,
            _ => throw new InvalidOperationException($"No embedded assembly resource is registered for '{simpleName}'.")
        };
    }

    private static byte[] ReadEmbeddedResourceBytes(string resourceName)
    {
        using Stream? stream = typeof(Subnautica2UharaBridge).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new FileNotFoundException($"Embedded resource '{resourceName}' was not found.");

        using MemoryStream buffer = new();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
