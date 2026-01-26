using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros
{
    public static class ResetMacroService
    {
        public static async Task RunAsync(GameMode mode)
        {
            var proc = Process.GetProcessesByName("Subnautica");
            if (proc.Length == 0)
                return;

            string root = Path.GetDirectoryName(proc[0].MainModule!.FileName!)!;
            int yearGroup = BuildYearResolver.ResolveGroupedYear(root);

            var profile = GameStateDetectorRegistry.Get(yearGroup);
            var steps = MacroRegistry.Get(yearGroup, mode);

            var state = GameStateDetector.Detect(profile);

            // ================= MAIN MENU: INSTANT PATH =================
            if (state == GameState.MainMenu)
            {
                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                return;
            }

            // ================= IN-GAME QUIT FLOW =================
            if (state == GameState.InGame)
            {
                NativeInput.PressEsc();
                await Task.Delay(200);

                await NativeInput.Click(steps.QuitButton, steps.ClickDelayMedium);
                await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelaySlow);
                await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelaySlow);
            }

            // ================= BLACK SCREEN DETECTION =================
            bool sawBlackScreen = false;
            var blackWait = Stopwatch.StartNew();

            while (blackWait.ElapsedMilliseconds < 5000)
            {
                if (GameStateDetector.IsBlackScreen(profile))
                {
                    sawBlackScreen = true;
                    break;
                }

                await Task.Delay(50);
            }

            if (sawBlackScreen)
            {
                while (GameStateDetector.IsBlackScreen(profile))
                    await Task.Delay(50);

                await Task.Delay(250);
            }
            else
            {
                // Fallback main menu detection
                var menuWait = Stopwatch.StartNew();

                while (menuWait.ElapsedMilliseconds < 5000)
                {
                    if (GameStateDetector.Detect(profile) == GameState.MainMenu)
                        break;

                    await Task.Delay(50);
                }

                await Task.Delay(250);
            }

            // ================= START NEW GAME =================
            await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
            await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
            await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
        }
    }
}