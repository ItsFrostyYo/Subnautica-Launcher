using SubnauticaLauncher.Display;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Macros
{
    [SupportedOSPlatform("windows")]
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

            // 🔥 GET DISPLAY SCALE (ONCE)
            var display = DisplayInfo.GetPrimary();

            // 🔥 2022+ loads instantly — add visibility delay
            bool needsGamemodeDelay = yearGroup >= 2022;

            var state = GameStateDetector.Detect(profile, display);

            // ================= MAIN MENU: INSTANT PATH =================
            if (state == GameState.MainMenu)
            {
                await NativeInput.Click(display.ScalePoint(steps.PlayButton), steps.ClickDelayFast);
                await NativeInput.Click(display.ScalePoint(steps.StartNewGame), steps.ClickDelaySlow);

                if (needsGamemodeDelay)
                    await Task.Delay(250); // 👀 verifier-visible delay

                await NativeInput.Click(display.ScalePoint(steps.SelectGameMode), steps.ClickDelayMedium);
                return;
            }

            // ================= IN-GAME QUIT FLOW =================
            if (state == GameState.InGame)
            {
                NativeInput.PressEsc();
                await Task.Delay(200);

                await NativeInput.Click(display.ScalePoint(steps.QuitButton), steps.ClickDelayMedium);
                await NativeInput.Click(display.ScalePoint(steps.ConfirmQuit1), steps.ClickDelaySlow);
                await NativeInput.Click(display.ScalePoint(steps.ConfirmQuit2), steps.ClickDelaySlow);
            }

            // ================= BLACK SCREEN DETECTION =================
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
                while (GameStateDetector.IsBlackScreen(profile, display))
                    await Task.Delay(50);

                await Task.Delay(250);
            }
            else
            {
                var menuWait = Stopwatch.StartNew();

                while (menuWait.ElapsedMilliseconds < 5000)
                {
                    if (GameStateDetector.Detect(profile, display) == GameState.MainMenu)
                        break;

                    await Task.Delay(50);
                }

                await Task.Delay(250);
            }

            // ================= START NEW GAME =================
            await NativeInput.Click(display.ScalePoint(steps.PlayButton), steps.ClickDelayFast);
            await NativeInput.Click(display.ScalePoint(steps.StartNewGame), steps.ClickDelaySlow);

            if (needsGamemodeDelay)
                await Task.Delay(250); // 👀 verifier-visible delay

            await NativeInput.Click(display.ScalePoint(steps.SelectGameMode), steps.ClickDelayMedium);
        }
    }
}