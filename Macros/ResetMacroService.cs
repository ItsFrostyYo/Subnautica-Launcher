using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Gameplay;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
    public static class ResetMacroService
    {
        private const int MainMenuReadyTimeoutMs = 5000;
        private const int MainMenuReadyPollMs = 25;
        private const int RequiredReadySamples = 2;

        public static async Task RunAsync(
            GameMode mode,
            ResetMacroLogChannel logChannel = ResetMacroLogChannel.Subnautica)
        {
            if (!GameProcessMonitor.TryOpenRunningProcess("Subnautica", out Process? openedProcess) || openedProcess == null)
            {
                ResetMacroLogger.Warn(
                    logChannel,
                    "Reset requested but Subnautica process is not running.");
                return;
            }

            Process process = openedProcess;

            try
            {
                string root = Path.GetDirectoryName(process.MainModule!.FileName!)!;
                int yearGroup = BuildYearResolver.ResolveGroupedYear(root);

                var steps = MacroRegistry.Get(yearGroup, mode);
                bool needsGameModeDelay = yearGroup >= 2022;

                GameStateDetector.TryDetectStateFromMemory(process, "Subnautica", out GameState state);
                bool startedInGame = state == GameState.InGame;

                ResetMacroLogger.Info(
                    logChannel,
                    $"Start reset. Mode={mode}, PID={process.Id}, YearGroup={yearGroup}, InitialState={state}.");

                if (state == GameState.MainMenu)
                {
                    MainMenuReadyResult menuReady = await WaitForMainMenuPlayButtonAsync(process);

                    if (!menuReady.IsReady)
                    {
                        ResetMacroLogger.Warn(
                            logChannel,
                            $"Main menu was detected, but its input state did not become ready in memory within {menuReady.ElapsedMilliseconds}ms. Reset was cancelled before input was sent.");
                        return;
                    }

                    ResetMacroLogger.Info(
                        logChannel,
                        $"State=MainMenu. Play input confirmed ready in memory in {menuReady.ElapsedMilliseconds}ms. Running direct new-game flow.");

                    await StartNewGameAsync(process, steps, needsGameModeDelay);
                    ResetMacroLogger.Info(logChannel, "Reset completed from main menu path.");
                    return;
                }

                if (state == GameState.InGame)
                {
                    ResetMacroLogger.Info(logChannel, "State=InGame. Running quit-to-menu flow.");

                    NativeInput.PressEsc();
                    await Task.Delay(50);

                    await NativeInput.Click(process, steps.QuitButton, steps.ClickDelayMedium);
                    await Task.Delay(50);
                    await NativeInput.Click(process, steps.ConfirmQuit1, steps.ClickDelayMedium);
                    await Task.Delay(50);
                    await NativeInput.Click(process, steps.ConfirmQuit2, steps.ClickDelayMedium);
                }
                else
                {
                    ResetMacroLogger.Warn(
                        logChannel,
                        $"State={state}. Continuing with fallback synchronization flow.");
                }

                MainMenuReadyResult menuReadyAfterQuit = await WaitForMainMenuPlayButtonAsync(process);

                if (menuReadyAfterQuit.IsReady)
                {
                    ResetMacroLogger.Info(
                        logChannel,
                        $"Main menu Play input confirmed ready in memory in {menuReadyAfterQuit.ElapsedMilliseconds}ms.");
                }
                else
                {
                    ResetMacroLogger.Warn(
                        logChannel,
                        $"Main menu Play input was not confirmed ready in memory within {menuReadyAfterQuit.ElapsedMilliseconds}ms. Reset was cancelled before input was sent.");
                    return;
                }

                string? slotToDelete = startedInGame
                    ? HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode)
                    : null;

                if (!string.IsNullOrWhiteSpace(slotToDelete))
                {
                    ResetMacroLogger.Info(
                        logChannel,
                        $"Queued hardcore slot delete after restart: {slotToDelete}");
                }

                await StartNewGameAsync(process, steps, needsGameModeDelay);
                await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete, logChannel: logChannel);

                ResetMacroLogger.Info(logChannel, "Reset completed successfully.");
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(logChannel, ex, "Reset macro failed.");
                throw;
            }
            finally
            {
                process.Dispose();
            }
        }

        private static async Task StartNewGameAsync(
            Process process,
            MacroSteps steps,
            bool needsGameModeDelay)
        {
            await NativeInput.Click(process, steps.PlayButton, steps.ClickDelayFast);
            await Task.Delay(50);
            await NativeInput.Click(process, steps.StartNewGame, steps.ClickDelaySlow);
            await Task.Delay(50);
            if (needsGameModeDelay)
                await Task.Delay(100);

            await NativeInput.Click(process, steps.SelectGameMode, steps.ClickDelayMedium);
        }

        private static async Task<MainMenuReadyResult> WaitForMainMenuPlayButtonAsync(
            Process process)
        {
            int readySamples = 0;
            var wait = Stopwatch.StartNew();

            while (wait.ElapsedMilliseconds < MainMenuReadyTimeoutMs)
            {
                bool playInputReady = GameStateDetector.TryDetectMainMenuPlayReadyFromMemory(
                    process,
                    "Subnautica",
                    out bool isReady) && isReady;

                readySamples = playInputReady ? readySamples + 1 : 0;
                if (readySamples >= RequiredReadySamples)
                    return new MainMenuReadyResult(true, wait.ElapsedMilliseconds);

                await Task.Delay(MainMenuReadyPollMs);
            }

            return new MainMenuReadyResult(false, wait.ElapsedMilliseconds);
        }

        private readonly record struct MainMenuReadyResult(
            bool IsReady,
            long ElapsedMilliseconds);
    }
}
