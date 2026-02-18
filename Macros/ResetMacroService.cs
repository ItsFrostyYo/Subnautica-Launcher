using SubnauticaLauncher.Display;
using SubnauticaLauncher.Enums;
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
            var processes = Process.GetProcessesByName("Subnautica");
            if (processes.Length == 0)
            {
                ResetMacroLogger.Warn(
                    logChannel,
                    "Reset requested but Subnautica process is not running.");
                return;
            }

            try
            {
                Process process = processes[0];
                string root = Path.GetDirectoryName(process.MainModule!.FileName!)!;
                int yearGroup = BuildYearResolver.ResolveGroupedYear(root);

                var profile = GameStateDetectorRegistry.Get(yearGroup);
                var steps = MacroRegistry.Get(yearGroup, mode);
                var display = DisplayInfo.GetPrimary();
                bool needsGameModeDelay = yearGroup >= 2022;

                var state = GameStateDetector.Detect(profile, display);
                bool startedInGame = state == GameState.InGame;

                ResetMacroLogger.Info(
                    logChannel,
                    $"Start reset. Mode={mode}, PID={process.Id}, YearGroup={yearGroup}, InitialState={state}.");

                if (state == GameState.MainMenu)
                {
                    ResetMacroLogger.Info(logChannel, "State=MainMenu. Running direct new-game flow.");

                    await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                    await Task.Delay(50);
                    await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                    await Task.Delay(50);
                    if (needsGameModeDelay)
                        await Task.Delay(100);

                    await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                    ResetMacroLogger.Info(logChannel, "Reset completed from main menu path.");
                    return;
                }

                if (state == GameState.InGame)
                {
                    ResetMacroLogger.Info(logChannel, "State=InGame. Running quit-to-menu flow.");

                    NativeInput.PressEsc();
                    await Task.Delay(50);

                    await NativeInput.Click(steps.QuitButton, steps.ClickDelayMedium);
                    await Task.Delay(50);
                    await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                    await Task.Delay(50);
                    await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);
                }
                else
                {
                    ResetMacroLogger.Warn(
                        logChannel,
                        $"State={state}. Continuing with fallback synchronization flow.");
                }

                bool sawBlackScreen = false;
                var blackWait = Stopwatch.StartNew();

                while (blackWait.ElapsedMilliseconds < 5000)
                {
                    if (GameStateDetector.IsBlackScreen(profile, display))
                    {
                        sawBlackScreen = true;
                        break;
                    }

                    await Task.Delay(50);
                }

                if (sawBlackScreen)
                {
                    ResetMacroLogger.Info(
                        logChannel,
                        $"Black screen detected after {blackWait.ElapsedMilliseconds}ms. Waiting for menu return.");

                    while (GameStateDetector.IsBlackScreen(profile, display))
                        await Task.Delay(50);

                    await Task.Delay(150);
                }
                else
                {
                    var menuWait = Stopwatch.StartNew();
                    bool sawMenu = false;

                    while (menuWait.ElapsedMilliseconds < 5000)
                    {
                        if (GameStateDetector.Detect(profile, display) == GameState.MainMenu)
                        {
                            sawMenu = true;
                            break;
                        }

                        await Task.Delay(50);
                    }

                    if (!sawMenu)
                    {
                        ResetMacroLogger.Warn(
                            logChannel,
                            $"Main menu was not detected within {menuWait.ElapsedMilliseconds}ms.");
                    }

                    await Task.Delay(150);
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

                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await Task.Delay(50);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                await Task.Delay(50);
                if (needsGameModeDelay)
                    await Task.Delay(100);

                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete, logChannel: logChannel);

                ResetMacroLogger.Info(logChannel, "Reset completed successfully.");
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(logChannel, ex, "Reset macro failed.");
                throw;
            }
        }
    }
}
