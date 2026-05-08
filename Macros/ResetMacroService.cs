using SubnauticaLauncher.Display;
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

                var profile = GameStateDetectorRegistry.Get(yearGroup);
                var steps = MacroRegistry.Get(yearGroup, mode);
                var display = DisplayInfo.GetPrimary();
                bool needsGameModeDelay = yearGroup >= 2022;

                var state = GameStateDetector.Detect(process, "Subnautica", profile, display);
                bool startedInGame = state == GameState.InGame;

                ResetMacroLogger.Info(
                    logChannel,
                    $"Start reset. Mode={mode}, PID={process.Id}, YearGroup={yearGroup}, InitialState={state}.");

                if (state == GameState.MainMenu)
                {
                    ResetMacroLogger.Info(logChannel, "State=MainMenu. Running direct new-game flow.");

                    await NativeInput.Click(process, steps.PlayButton, steps.ClickDelayFast);
                    await Task.Delay(50);
                    await NativeInput.Click(process, steps.StartNewGame, steps.ClickDelaySlow);
                    await Task.Delay(50);
                    if (needsGameModeDelay)
                        await Task.Delay(100);

                    await NativeInput.Click(process, steps.SelectGameMode, steps.ClickDelayMedium);
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

                bool sawBlackScreen = false;
                bool sawMenu = false;
                var transitionWait = Stopwatch.StartNew();

                while (transitionWait.ElapsedMilliseconds < 5000)
                {
                    GameState transitionState = GameStateDetector.Detect(
                        process,
                        "Subnautica",
                        profile,
                        display,
                        focusGame: false);

                    if (transitionState == GameState.MainMenu)
                    {
                        sawMenu = true;
                        break;
                    }

                    if (transitionState == GameState.BlackScreen)
                        sawBlackScreen = true;

                    await Task.Delay(50);
                }

                if (sawMenu)
                {
                    ResetMacroLogger.Info(
                        logChannel,
                        sawBlackScreen
                            ? $"Main menu detected after black-screen transition in {transitionWait.ElapsedMilliseconds}ms."
                            : $"Main menu detected directly in {transitionWait.ElapsedMilliseconds}ms.");
                }
                else
                {
                    ResetMacroLogger.Warn(
                        logChannel,
                        sawBlackScreen
                            ? $"Main menu was not detected within {transitionWait.ElapsedMilliseconds}ms after black-screen transition."
                            : $"Main menu was not detected within {transitionWait.ElapsedMilliseconds}ms.");
                }

                await Task.Delay(150);

                string? slotToDelete = startedInGame
                    ? HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode)
                    : null;

                if (!string.IsNullOrWhiteSpace(slotToDelete))
                {
                    ResetMacroLogger.Info(
                        logChannel,
                        $"Queued hardcore slot delete after restart: {slotToDelete}");
                }

                await NativeInput.Click(process, steps.PlayButton, steps.ClickDelayFast);
                await Task.Delay(50);
                await NativeInput.Click(process, steps.StartNewGame, steps.ClickDelaySlow);
                await Task.Delay(50);
                if (needsGameModeDelay)
                    await Task.Delay(100);

                await NativeInput.Click(process, steps.SelectGameMode, steps.ClickDelayMedium);
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
    }
}
