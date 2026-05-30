using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros;

[SupportedOSPlatform("windows")]
public static class Subnautica2ResetMacroService
{
    private const ResetMacroLogChannel LogChannel = ResetMacroLogChannel.Subnautica2;
    private const int FastPollMs = 10;
    private static readonly IReadOnlyList<CharacterOption> CharacterSelectors =
    [
        new(
            "Triage",
            steps => steps.SelectCharacterTriage,
            Subnautica2UharaBridge.Watchers.CharacterSelectButton1Clicked),
        new(
            "Felix",
            steps => steps.SelectCharacterFelix,
            Subnautica2UharaBridge.Watchers.CharacterSelectButton2Clicked),
        new(
            "Stone",
            steps => steps.SelectCharacterStone,
            Subnautica2UharaBridge.Watchers.CharacterSelectButtonDefaultClicked),
        new(
            "Tether",
            steps => steps.SelectCharacterTether,
            Subnautica2UharaBridge.Watchers.CharacterSelectButton3Clicked)
    ];

    private readonly record struct CharacterOption(
        string Name,
        Func<MacroSteps, System.Drawing.Point> Selector,
        string WatcherName);

    private static readonly string[] CharacterConfirmReadyWatchers =
    [
        Subnautica2UharaBridge.Watchers.CharacterConfirmReady,
        Subnautica2UharaBridge.Watchers.CharacterConfirmReadyButton1,
        Subnautica2UharaBridge.Watchers.CharacterConfirmReadyButton2,
        Subnautica2UharaBridge.Watchers.CharacterConfirmReadyButton3A,
        Subnautica2UharaBridge.Watchers.CharacterConfirmReadyButton3B
    ];

    public static async Task RunAsync(GameMode mode)
    {
        if (mode is not (GameMode.Survival or GameMode.Creative))
        {
            ResetMacroLogger.Warn(
                LogChannel,
                $"SN2 reset currently supports only Survival and Creative. Selected mode={mode}.");
            return;
        }

        string processName = LauncherGameProfiles.Subnautica2.ProcessName;
        if (!GameProcessMonitor.TryOpenRunningProcess(processName, out Process? openedProcess) || openedProcess == null)
        {
            ResetMacroLogger.Warn(
                LogChannel,
                "Reset requested but Subnautica 2 process is not running.");
            return;
        }

        Process process = openedProcess;

        try
        {
            MacroSteps steps = MacroRegistry.Get(BuildYearResolver.SUBNAUTICA2_GROUP, mode);
            Subnautica2UharaBridge bridge = Subnautica2UharaBridge.Shared;

            GameState state = await DetectStateAsync(bridge, process);
            bool startedInGame = state == GameState.InGame;

            ResetMacroLogger.Info(
                LogChannel,
                $"Start reset. Mode={mode}, PID={process.Id}, InitialState={state}, Reader=uhara10");

            if (state == GameState.MainMenu)
            {
                ResetMacroLogger.Info(LogChannel, "State=MainMenu. Running direct new-game flow.");
                await RunStartFlowAsync(process, steps, mode, bridge);
                ResetMacroLogger.Info(LogChannel, "Reset completed from main menu path.");
                return;
            }

            if (state != GameState.InGame)
            {
                ResetMacroLogger.Warn(
                    LogChannel,
                    $"Unable to determine SN2 state from memory events. LastState={state}. Aborting reset.");
                return;
            }

            ResetMacroLogger.Info(LogChannel, "State=InGame. Running quit-to-menu flow.");

            NativeInput.PressEsc();
            await Task.Delay(50);
            await NativeInput.Click(process, steps.QuitButton, steps.ClickDelayMedium);
            await Task.Delay(300);
            await NativeInput.Click(process, steps.ConfirmQuit1, steps.ClickDelayMedium);

            bool sawMenu = await WaitForMenuAsync(bridge, process, 8000);
            if (!sawMenu)
            {
                ResetMacroLogger.Warn(LogChannel, "Main menu was not detected from memory events after quit.");
            }
            else
            {
                ResetMacroLogger.Info(LogChannel, "Main menu detected from memory events.");
            }

            ResetMacroLogger.Info(LogChannel, "State=MainMenu. Waiting 500 milliseconds for button readiness.");
            await Task.Delay(500);
            await RunStartFlowAsync(process, steps, mode, bridge);

            if (startedInGame)
                ResetMacroLogger.Info(LogChannel, "Reset completed successfully after in-game quit path.");
        }
        catch (Exception ex)
        {
            ResetMacroLogger.Exception(LogChannel, ex, "Subnautica 2 reset macro failed.");
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task RunStartFlowAsync(
        Process process,
        MacroSteps steps,
        GameMode mode,
        Subnautica2UharaBridge bridge)
    {
        await WaitForTickReadinessAsync(
            bridge,
            process,
            Subnautica2UharaBridge.Watchers.MainMenuTick,
            1000);

        await ClickUntilWatcherAdvancesAsync(
            process,
            steps.PlayButton,
            steps.ClickDelayFast,
            bridge,
            Subnautica2UharaBridge.Watchers.PlaySinglePlayerClicked,
            maxAttempts: 50,
            retryIntervalMs: 100);

        await Task.Delay(300);

        await ClickUntilWatcherAdvancesAsync(
            process,
            steps.StartNewGame,
            steps.ClickDelayMedium,
            bridge,
            Subnautica2UharaBridge.Watchers.NewGameClicked,
            maxAttempts: 40,
            retryIntervalMs: 100);

        await Task.Delay(300);

        if (mode == GameMode.Creative)
        {
            await ClickUntilWatcherAdvancesAsync(
                process,
                steps.SelectGameMode,
                steps.ClickDelayMedium,
                bridge,
                Subnautica2UharaBridge.Watchers.CreativeModeSelected,
                maxAttempts: 30,
                retryIntervalMs: 100);

            await Task.Delay(150);
        }

        bridge.Refresh(process);
        Dictionary<string, ulong> buildNumberBaseline = CaptureCounters(
            bridge,
            [Subnautica2UharaBridge.Watchers.BuildNumberVisible]);

        await ClickUntilWatcherAdvancesAsync(
            process,
            steps.ConfirmStart,
            steps.ClickDelayMedium,
            bridge,
            Subnautica2UharaBridge.Watchers.ConfirmStartClicked,
            maxAttempts: 40,
            retryIntervalMs: 100);

        bool sawBuildNumber = await WaitForWatcherAdvanceAsync(
            bridge,
            process,
            Subnautica2UharaBridge.Watchers.BuildNumberVisible,
            timeoutMs: 10000,
            required: false,
            baselineOverride: buildNumberBaseline);

        if (!sawBuildNumber)
        {
            ResetMacroLogger.Warn(
                LogChannel,
                "Build number event did not appear within 10 seconds. Using Space fallback anyway.");
        }

        NativeInput.FocusGame(process);
        await Task.Delay(10);

        bool sawCharacterSelect = await PressSpaceUntilCharacterSelectAsync(
            bridge,
            process,
            timeoutMs: 3500,
            pressIntervalMs: 100);

        if (!sawCharacterSelect)
            throw new InvalidOperationException("Character select did not appear after confirm start.");

        ResetMacroLogger.Info(
            LogChannel,
            "Character select detected. Waiting 300ms for buttons to finish becoming clickable.");
        await Task.Delay(300);

        int selectedIndex = Random.Shared.Next(CharacterSelectors.Count);
        CharacterOption selectedCharacter = CharacterSelectors[selectedIndex];
        NativeInput.FocusGame(process);
        await Task.Delay(40);
        bridge.Refresh(process);
        Dictionary<string, ulong> selectedCharacterBaseline = CaptureCounters(
            bridge,
            CombineWatchers(selectedCharacter.WatcherName, CharacterConfirmReadyWatchers));
        Dictionary<string, ulong> confirmReadyBaseline = CaptureCounters(
            bridge,
            CharacterConfirmReadyWatchers);

        bool sawSelectionSignal = await ClickUntilAnyWatcherAdvancesAsync(
            process,
            selectedCharacter.Selector(steps),
            steps.ClickDelayMedium,
            bridge,
            CombineWatchers(selectedCharacter.WatcherName, CharacterConfirmReadyWatchers),
            maxAttempts: 20,
            retryIntervalMs: 100,
            required: false,
            baselineOverride: selectedCharacterBaseline,
            focusWindow: false,
            mouseDownDurationMs: 35);

        if (!sawSelectionSignal)
        {
            ResetMacroLogger.Warn(
                LogChannel,
                $"No SN2 selection watcher fired for {selectedCharacter.Name}, but continuing to confirm because the character may still be selected visually.");
        }

        ResetMacroLogger.Info(
            LogChannel,
            $"Selected random SN2 character {selectedCharacter.Name}.");

        await Task.Delay(200);

        await WaitForAnyWatcherAdvanceAsync(
            bridge,
            process,
            CharacterConfirmReadyWatchers,
            timeoutMs: 900,
            required: false,
            baselineOverride: confirmReadyBaseline);

        bool sawConfirmSignal = await ClickUntilAnyWatcherAdvancesAsync(
            process,
            steps.ConfirmCharacter,
            steps.ClickDelayMedium,
            bridge,
            [
                Subnautica2UharaBridge.Watchers.CharacterConfirmClicked5,
                Subnautica2UharaBridge.Watchers.CharacterConfirmClicked6,
                Subnautica2UharaBridge.Watchers.CharacterConfirmClicked7
            ],
            maxAttempts: 20,
            retryIntervalMs: 100,
            required: false,
            baselineOverride: null,
            focusWindow: false,
            mouseDownDurationMs: 35);

        if (!sawConfirmSignal)
        {
            ResetMacroLogger.Warn(
                LogChannel,
                "No SN2 confirm watcher fired, but confirm clicks were still sent. Continuing with post-confirm Esc sequence.");
        }

        await Task.Delay(350);
        NativeInput.PressEsc();
        await Task.Delay(100);
        NativeInput.PressEsc();

        ResetMacroLogger.Info(LogChannel, $"Issued memory-driven start flow for {mode}.");
    }

    private static async Task<GameState> DetectStateAsync(
        Subnautica2UharaBridge bridge,
        Process process)
    {
        bridge.Refresh(process);
        ulong tickBaseline = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainMenuTick);
        ulong constructBaseline = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainLobbyConstruct);

        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 600)
        {
            await Task.Delay(25);
            bridge.Refresh(process);

            ulong tick = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainMenuTick);
            ulong construct = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainLobbyConstruct);
            if (tick > tickBaseline || construct > constructBaseline)
                return GameState.MainMenu;
        }

        return GameState.InGame;
    }

    private static async Task<bool> WaitForMenuAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        int timeoutMs)
    {
        bridge.Refresh(process);
        ulong tickBaseline = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainMenuTick);
        ulong constructBaseline = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainLobbyConstruct);

        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(FastPollMs);
            bridge.Refresh(process);

            ulong tick = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainMenuTick);
            ulong construct = bridge.GetCounter(Subnautica2UharaBridge.Watchers.MainLobbyConstruct);
            if (tick > tickBaseline || construct > constructBaseline)
            {
                ResetMacroLogger.Info(
                    LogChannel,
                    $"Main menu detected from memory events in {timer.ElapsedMilliseconds}ms.");
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> WaitForWatcherAdvanceAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        string watcherName,
        int timeoutMs,
        bool required,
        IReadOnlyDictionary<string, ulong>? baselineOverride = null)
    {
        return await WaitForAnyWatcherAdvanceAsync(
            bridge,
            process,
            [watcherName],
            timeoutMs,
            required,
            baselineOverride);
    }

    private static async Task<bool> WaitForAnyWatcherAdvanceAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        IReadOnlyList<string> watcherNames,
        int timeoutMs,
        bool required,
        IReadOnlyDictionary<string, ulong>? baselineOverride = null)
    {
        bridge.Refresh(process);
        IReadOnlyDictionary<string, ulong> baseline = baselineOverride ?? CaptureCounters(bridge, watcherNames);
        if (TryDetectWatcherSignal(bridge, baseline, watcherNames, out string? immediateWatcher, out string? immediateSource))
        {
            ResetMacroLogger.Info(
                LogChannel,
                $"Watcher signaled immediately via {immediateSource}: {immediateWatcher}");
            return true;
        }

        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(FastPollMs);
            bridge.Refresh(process);
            if (TryDetectWatcherSignal(bridge, baseline, watcherNames, out string? advancedWatcher, out string? signalSource))
            {
                ResetMacroLogger.Info(
                    LogChannel,
                    $"Watcher signaled via {signalSource}: {advancedWatcher}");
                return true;
            }
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for SN2 watchers '{string.Join(", ", watcherNames)}' after {timeoutMs}ms.");
        }

        return false;
    }

    private static async Task<bool> WaitForCharacterSelectAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        int timeoutMs,
        IReadOnlyDictionary<string, ulong>? baselineOverride = null)
    {
        return await WaitForWatcherAdvanceAsync(
            bridge,
            process,
            Subnautica2UharaBridge.Watchers.CharacterSelectReady,
            timeoutMs,
            required: false,
            baselineOverride);
    }

    private static async Task<bool> ClickUntilWatcherAdvancesAsync(
        Process process,
        System.Drawing.Point point,
        int clickDelayMs,
        Subnautica2UharaBridge bridge,
        string watcherName,
        int maxAttempts,
        int retryIntervalMs,
        bool required = true,
        IReadOnlyDictionary<string, ulong>? baselineOverride = null,
        bool focusWindow = true,
        int mouseDownDurationMs = 5)
    {
        return await ClickUntilAnyWatcherAdvancesAsync(
            process,
            point,
            clickDelayMs,
            bridge,
            [watcherName],
            maxAttempts,
            retryIntervalMs,
            required,
            baselineOverride,
            focusWindow,
            mouseDownDurationMs);
    }

    private static async Task<bool> ClickUntilAnyWatcherAdvancesAsync(
        Process process,
        System.Drawing.Point point,
        int clickDelayMs,
        Subnautica2UharaBridge bridge,
        IReadOnlyList<string> watcherNames,
        int maxAttempts,
        int retryIntervalMs,
        bool required = true,
        IReadOnlyDictionary<string, ulong>? baselineOverride = null,
        bool focusWindow = true,
        int mouseDownDurationMs = 5)
    {
        bridge.Refresh(process);
        Dictionary<string, ulong> baseline = baselineOverride is Dictionary<string, ulong> typedBaseline
            ? new Dictionary<string, ulong>(typedBaseline, StringComparer.Ordinal)
            : new Dictionary<string, ulong>(baselineOverride ?? CaptureCounters(bridge, watcherNames), StringComparer.Ordinal);

        for (int attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
        {
            ResetMacroLogger.Info(
                LogChannel,
                $"Click attempt {attempt}/{maxAttempts} for watchers: {string.Join(", ", watcherNames)}");

            await NativeInput.Click(process, point, clickDelayMs, focusWindow, mouseDownDurationMs);
            if (await WaitForAnyWatcherAdvanceAsync(
                bridge,
                process,
                watcherNames,
                timeoutMs: retryIntervalMs,
                required: false,
                baselineOverride: baseline))
            {
                return true;
            }

            bridge.Refresh(process);
            baseline = CaptureCounters(bridge, watcherNames);
        }

        if (required)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for SN2 click watchers '{string.Join(", ", watcherNames)}' after {maxAttempts} attempts.");
        }

        return false;
    }

    private static async Task<bool> PressSpaceUntilCharacterSelectAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        int timeoutMs,
        int pressIntervalMs)
    {
        bridge.Refresh(process);
        Dictionary<string, ulong> baseline = CaptureCounters(
            bridge,
            [Subnautica2UharaBridge.Watchers.CharacterSelectReady]);

        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMs)
        {
            NativeInput.PressSpace();
            if (await WaitForCharacterSelectAsync(
                bridge,
                process,
                pressIntervalMs,
                baseline))
            {
                return true;
            }

            bridge.Refresh(process);
            baseline = CaptureCounters(
                bridge,
                [Subnautica2UharaBridge.Watchers.CharacterSelectReady]);
        }

        return false;
    }

    private static async Task WaitForTickReadinessAsync(
        Subnautica2UharaBridge bridge,
        Process process,
        string tickWatcherName,
        int timeoutMs)
    {
        bool ready = await WaitForWatcherAdvanceAsync(
            bridge,
            process,
            tickWatcherName,
            timeoutMs,
            required: false);

        if (!ready)
        {
            ResetMacroLogger.Warn(
                LogChannel,
                $"Tick watcher '{tickWatcherName}' did not visibly advance before clicking.");
        }
    }

    private static Dictionary<string, ulong> CaptureCounters(
        Subnautica2UharaBridge bridge,
        IReadOnlyList<string> watcherNames)
    {
        Dictionary<string, ulong> counters = new(StringComparer.Ordinal);
        foreach (string watcherName in watcherNames)
            counters[watcherName] = bridge.GetCounter(watcherName);

        return counters;
    }

    private static bool HaveWatchersAdvanced(
        Subnautica2UharaBridge bridge,
        IReadOnlyDictionary<string, ulong> baseline,
        IReadOnlyList<string> watcherNames,
        out string? advancedWatcher)
    {
        foreach (string watcherName in watcherNames)
        {
            ulong current = bridge.GetCounter(watcherName);
            ulong previous = baseline.TryGetValue(watcherName, out ulong value) ? value : 0;
            if (current > previous)
            {
                advancedWatcher = watcherName;
                return true;
            }
        }

        advancedWatcher = null;
        return false;
    }

    private static bool TryDetectWatcherSignal(
        Subnautica2UharaBridge bridge,
        IReadOnlyDictionary<string, ulong> baseline,
        IReadOnlyList<string> watcherNames,
        out string? signaledWatcher,
        out string? signalSource)
    {
        foreach (string watcherName in watcherNames)
        {
            if (bridge.CheckFlag(watcherName))
            {
                signaledWatcher = watcherName;
                signalSource = "flag";
                return true;
            }
        }

        if (HaveWatchersAdvanced(bridge, baseline, watcherNames, out signaledWatcher))
        {
            signalSource = "counter";
            return true;
        }

        signaledWatcher = null;
        signalSource = null;
        return false;
    }

    private static string[] CombineWatchers(string firstWatcher, IReadOnlyList<string> additionalWatchers)
    {
        string[] combined = new string[additionalWatchers.Count + 1];
        combined[0] = firstWatcher;
        for (int i = 0; i < additionalWatchers.Count; i++)
            combined[i + 1] = additionalWatchers[i];

        return combined;
    }
}
