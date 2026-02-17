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
            var process = Process.GetProcessesByName("SubnauticaZero");
            if (process.Length == 0)
                return;

            string root = Path.GetDirectoryName(process[0].MainModule!.FileName!)!;
            int buildYear = ReadBelowZeroBuildYear(root);

            bool isLegacy = buildYear >= 2019 && buildYear <= 2021;
            bool isModern = buildYear >= 2022;

            const int BzGroup = -1;

            var profile = GameStateDetectorRegistry.Get(BzGroup);
            var steps = MacroRegistry.Get(BzGroup, mode);
            var display = DisplayInfo.GetPrimary();

            var state = GameStateDetector.Detect(profile, display);
            bool startedInGame = state == GameState.InGame;

            if (state == GameState.MainMenu)
            {
                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                await Task.Delay(150);
                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                return;
            }

            if (state == GameState.InGame)
            {
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

            bool sawBlack = false;
            var sw = Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < 5000)
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
                while (GameStateDetector.IsBlackScreen(profile, display))
                    await Task.Delay(50);

                await Task.Delay(150);
            }
            else
            {
                var wait = Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 5000)
                {
                    if (GameStateDetector.Detect(profile, display) == GameState.MainMenu)
                        break;

                    await Task.Delay(50);
                }

                await Task.Delay(150);
            }

            string? slotToDelete = startedInGame
                ? HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode)
                : null;

            await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
            await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
            await Task.Delay(150);
            await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
            await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete);
        }

        private static int ReadBelowZeroBuildYear(string root)
        {
            string[] paths =
            {
                Path.Combine(root, "__buildtime.txt"),
                Path.Combine(root, "SubnauticaZero_Data", "StreamingAssets", "__buildtime.txt")
            };

            foreach (var path in paths)
            {
                if (!File.Exists(path))
                    continue;

                string text = File.ReadAllText(path).Trim();

                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt.Year;
            }

            return 2022;
        }
    }
}
