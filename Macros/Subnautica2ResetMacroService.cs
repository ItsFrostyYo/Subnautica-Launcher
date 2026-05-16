using SubnauticaLauncher.Core;
using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros;

[SupportedOSPlatform("windows")]
public static class Subnautica2ResetMacroService
{
    private const ResetMacroLogChannel LogChannel = ResetMacroLogChannel.Subnautica2;

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
            Subnautica2LogStateReader logReader = Subnautica2LogStateReader.Shared;
            DateTime? processStartUtc = TryGetProcessStartUtc(process);

            Subnautica2LogSnapshot snapshot = await WaitForKnownStateAsync(logReader, processStartUtc, 4000);
            bool startedInGame = snapshot.State == GameState.InGame;

            ResetMacroLogger.Info(
                LogChannel,
                $"Start reset. Mode={mode}, PID={process.Id}, InitialState={snapshot.State}, Log={snapshot.LogPath}");

            if (snapshot.State == GameState.MainMenu)
            {
                ResetMacroLogger.Info(LogChannel, "State=MainMenu. Running direct new-game flow.");
                await RunStartFlowAsync(process, steps, mode);
                ResetMacroLogger.Info(LogChannel, "Reset completed from main menu path.");
                return;
            }

            if (snapshot.State != GameState.InGame)
            {
                ResetMacroLogger.Warn(
                    LogChannel,
                    $"Unable to determine SN2 state from logs. LastState={snapshot.State}. Aborting reset.");
                return;
            }

            ResetMacroLogger.Info(LogChannel, "State=InGame. Running quit-to-menu flow.");

            NativeInput.PressEsc();
            await Task.Delay(50);
            await NativeInput.Click(process, steps.QuitButton, steps.ClickDelayMedium);
            await Task.Delay(300);
            await NativeInput.Click(process, steps.ConfirmQuit1, steps.ClickDelayMedium);

            Subnautica2LogSnapshot menuSnapshot = await WaitForMainMenuAsync(logReader, processStartUtc);
            if (menuSnapshot.State != GameState.MainMenu)
            {
                ResetMacroLogger.Warn(LogChannel, "Main menu was not detected from logs after quit.");
            }
            else
            {
                bool sawReady = menuSnapshot.IsMainMenuReady ||
                                menuSnapshot.MainMenuReadyPulse ||
                                await WaitForMainMenuReadyAsync(logReader, processStartUtc);
                if (!sawReady)
                {
                    ResetMacroLogger.Warn(
                        LogChannel,
                        "Main menu became active, but no front-end ready pulse was observed before continuing.");
                }
            }

            ResetMacroLogger.Info(LogChannel, "State=MainMenu. Waiting 500 Milliseconds for button Readyness");
            await Task.Delay(500);
            await RunStartFlowAsync(process, steps, mode);

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
        GameMode mode)
    {
        await NativeInput.Click(process, steps.PlayButton, steps.ClickDelayFast);
        await Task.Delay(25);
        await NativeInput.Click(process, steps.PlayButton, steps.ClickDelayFast);
        await Task.Delay(300);

        await NativeInput.Click(process, steps.StartNewGame, steps.ClickDelayMedium);
        await Task.Delay(25);
        await NativeInput.Click(process, steps.StartNewGame, steps.ClickDelayMedium);
        await Task.Delay(300);

        if (mode == GameMode.Creative)
        {
            await NativeInput.Click(process, steps.SelectGameMode, steps.ClickDelayMedium);
            await Task.Delay(25);
            await NativeInput.Click(process, steps.SelectGameMode, steps.ClickDelayMedium);
            await Task.Delay(150);
        }

        await NativeInput.Click(process, steps.ConfirmStart, steps.ClickDelayMedium);
        await Task.Delay(100);
        await NativeInput.Click(process, steps.ConfirmStart, steps.ClickDelayMedium);
        ResetMacroLogger.Info(LogChannel, $"Issued start flow clicks for {mode}.");
    }

    private static async Task<Subnautica2LogSnapshot> WaitForKnownStateAsync(
        Subnautica2LogStateReader logReader,
        DateTime? processStartUtc,
        int timeoutMs)
    {
        Stopwatch timer = Stopwatch.StartNew();
        Subnautica2LogSnapshot snapshot = logReader.Update(processStartUtc);

        while (!snapshot.HasKnownState && timer.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(50);
            snapshot = logReader.Update(processStartUtc);
        }

        return snapshot;
    }

    private static async Task<Subnautica2LogSnapshot> WaitForMainMenuAsync(
        Subnautica2LogStateReader logReader,
        DateTime? processStartUtc)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 8000)
        {
            Subnautica2LogSnapshot snapshot = logReader.Update(processStartUtc);
            if (snapshot.MainMenuResetPulse)
                ResetMacroLogger.Info(LogChannel, "Main menu quit pulse detected.");

            if (snapshot.State == GameState.MainMenu)
            {
                ResetMacroLogger.Info(
                    LogChannel,
                    $"Main menu detected from logs in {timer.ElapsedMilliseconds}ms.");
                return snapshot;
            }

            await Task.Delay(50);
        }

        return default;
    }

    private static async Task<bool> WaitForMainMenuReadyAsync(
        Subnautica2LogStateReader logReader,
        DateTime? processStartUtc)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < 5000)
        {
            Subnautica2LogSnapshot snapshot = logReader.Update(processStartUtc);
            if (snapshot.IsMainMenuReady || snapshot.MainMenuReadyPulse)
            {
                ResetMacroLogger.Info(
                    LogChannel,
                    $"Main menu load-complete detected in {timer.ElapsedMilliseconds}ms.");
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    private static DateTime? TryGetProcessStartUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return null;
        }
    }
}
