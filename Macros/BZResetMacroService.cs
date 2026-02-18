using SubnauticaLauncher.Display;
using SubnauticaLauncher.Enums;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
    public static class BZResetMacroService
    {
        public static async Task RunAsync(GameMode mode)
        {
            const ResetMacroLogChannel LogChannel = ResetMacroLogChannel.BelowZero;

            var processes = Process.GetProcessesByName("SubnauticaZero");
            if (processes.Length == 0)
            {
                ResetMacroLogger.Warn(
                    LogChannel,
                    "Reset requested but Subnautica: Below Zero process is not running.");
                return;
            }

            try
            {
                Process process = processes[0];
                string root = Path.GetDirectoryName(process.MainModule!.FileName!)!;
                int buildYear = ReadBelowZeroBuildYear(root);

                bool isLegacy = buildYear >= 2019 && buildYear <= 2021;
                bool isModern = buildYear >= 2022;
                const int BzGroup = -1;

                var profile = GameStateDetectorRegistry.Get(BzGroup);
                var steps = MacroRegistry.Get(BzGroup, mode);
                var display = DisplayInfo.GetPrimary();

                var state = GameStateDetector.Detect("SubnauticaZero", profile, display);
                bool startedInGame = state == GameState.InGame;

                ResetMacroLogger.Info(
                    LogChannel,
                    $"Start reset. Mode={mode}, PID={process.Id}, BuildYear={buildYear}, InitialState={state}.");

                if (state == GameState.MainMenu)
                {
                    ResetMacroLogger.Info(LogChannel, "State=MainMenu. Running direct new-game flow.");

                    await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                    await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                    await Task.Delay(150);
                    await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);

                    ResetMacroLogger.Info(LogChannel, "Reset completed from main menu path.");
                    return;
                }

                if (state == GameState.InGame)
                {
                    ResetMacroLogger.Info(
                        LogChannel,
                        $"State=InGame. Running quit-to-menu flow for {(isLegacy ? "legacy" : "modern")} UI.");

                    NativeInput.PressEsc();
                    await Task.Delay(25);

                    if (isLegacy)
                    {
                        await NativeInput.Click(steps.QuitButton2, steps.ClickDelayMedium);
                        await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                        await Task.Delay(25);
                        await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);
                    }
                    else if (isModern)
                    {
                        await NativeInput.Click(steps.QuitButton, steps.ClickDelayMedium);
                        await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                        await Task.Delay(25);
                        await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);
                    }
                }
                else
                {
                    ResetMacroLogger.Warn(
                        LogChannel,
                        $"State={state}. Continuing with fallback synchronization flow.");
                }

                bool sawBlack = false;
                var blackWait = Stopwatch.StartNew();

                while (blackWait.ElapsedMilliseconds < 5000)
                {
                    if (GameStateDetector.IsBlackScreen(profile, display))
                    {
                        sawBlack = true;
                        break;
                    }

                    await Task.Delay(50);
                }

                if (sawBlack)
                {
                    ResetMacroLogger.Info(
                        LogChannel,
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
                        if (GameStateDetector.Detect("SubnauticaZero", profile, display) == GameState.MainMenu)
                        {
                            sawMenu = true;
                            break;
                        }

                        await Task.Delay(50);
                    }

                    if (!sawMenu)
                    {
                        ResetMacroLogger.Warn(
                            LogChannel,
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
                        LogChannel,
                        $"Queued hardcore slot delete after restart: {slotToDelete}");
                }

                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                await Task.Delay(150);
                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete, logChannel: LogChannel);

                ResetMacroLogger.Info(LogChannel, "Reset completed successfully.");
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(LogChannel, ex, "Below Zero reset macro failed.");
                throw;
            }
        }

        private static int ReadBelowZeroBuildYear(string root)
        {
            string[] paths =
            {
                Path.Combine(root, "__buildtime.txt"),
                Path.Combine(root, "SubnauticaZero_Data", "StreamingAssets", "__buildtime.txt")
            };

            foreach (string path in paths)
            {
                if (!File.Exists(path))
                    continue;

                string text = File.ReadAllText(path).Trim();
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
                    return dt.Year;
            }

            return 2022;
        }
    }
}
