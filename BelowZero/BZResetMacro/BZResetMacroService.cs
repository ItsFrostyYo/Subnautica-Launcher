using SubnauticaLauncher.Display;
using SubnauticaLauncher.Macros;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.BelowZero
{
    [SupportedOSPlatform("windows")]
    public static class BZResetMacroService
    {
        public static async Task RunAsync(GameMode mode)
        {
            // ✅ BELOW ZERO ONLY
            var proc = Process.GetProcessesByName("SubnauticaZero");
            if (proc.Length == 0)
                return;

            string root = Path.GetDirectoryName(proc[0].MainModule!.FileName!)!;

            // 🔎 READ BUILD YEAR (BZ ONLY)
            int buildYear = ReadBelowZeroBuildYear(root);

            bool isLegacy = buildYear >= 2019 && buildYear <= 2021;
            bool isModern = buildYear >= 2022;

            // 🔒 SINGLE BZ GROUP
            const int BZ_GROUP = -1;

            var profile = GameStateDetectorRegistry.Get(BZ_GROUP);
            var steps = MacroRegistry.Get(BZ_GROUP, mode);
            var display = DisplayInfo.GetPrimary();

            var state = GameStateDetector.Detect(profile, display);

            bool startedInGame = state == GameState.InGame;

            // ================= MAIN MENU =================
            if (state == GameState.MainMenu)
            {
                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);

                await Task.Delay(150);
                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                return;
            }

            // ================= IN-GAME QUIT =================
            if (state == GameState.InGame)
            {
                NativeInput.PressEsc();
                await Task.Delay(25);

                if (isLegacy)
                {
                    // 🔁 2019–2021 FLOW                    
                    await NativeInput.Click(steps.QuitButton2, steps.ClickDelayMedium);                    
                    await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                    await Task.Delay(25);
                    await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);
                }
                else if (isModern)
                {
                    // 🔁 2022+ FLOW
                    await NativeInput.Click(steps.QuitButton, steps.ClickDelayMedium);
                    await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                    await Task.Delay(25);
                    await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);
                }
            }

            // ================= BLACK SCREEN =================
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

            // ================= START NEW GAME =================
            string? slotToDelete = startedInGame
                ? HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode)
                : null;

            await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
            await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
            await Task.Delay(150);
            await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
            await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete);
        }

        // ================= BUILD YEAR (BZ ONLY) =================
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

                var text = File.ReadAllText(path).Trim();

                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return dt.Year;
            }

            // Safe default → modern
            return 2022;
        }
    }
}
