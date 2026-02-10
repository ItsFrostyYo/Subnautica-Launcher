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
                string? hardcoreSlotToDelete =
                    HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode);

                await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
                await Task.Delay(50);
                await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
                await Task.Delay(50);
                if (needsGamemodeDelay)
                    await Task.Delay(100); // 👀 verifier-visible delay

                await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
                await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(hardcoreSlotToDelete);
                return;
            }

            // ================= IN-GAME QUIT FLOW =================
            if (state == GameState.InGame)
            {
                NativeInput.PressEsc();
                await Task.Delay(50);
                
                await NativeInput.Click(steps.QuitButton, steps.ClickDelayMedium);
                await Task.Delay(50);
                await NativeInput.Click(steps.ConfirmQuit1, steps.ClickDelayMedium);
                await Task.Delay(50);
                await NativeInput.Click(steps.ConfirmQuit2, steps.ClickDelayMedium);                
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

                await Task.Delay(150);
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

                await Task.Delay(150);
            }

            // ================= START NEW GAME =================
            string? slotToDelete =
                HardcoreSaveDeleter.GetLatestHardcoreSlotToDelete(root, mode);

            await NativeInput.Click(steps.PlayButton, steps.ClickDelayFast);
            await Task.Delay(50);
            await NativeInput.Click(steps.StartNewGame, steps.ClickDelaySlow);
            await Task.Delay(50);
            if (needsGamemodeDelay)
                await Task.Delay(100); // 👀 verifier-visible delay
            
            await NativeInput.Click(steps.SelectGameMode, steps.ClickDelayMedium);
            await HardcoreSaveDeleter.DeleteSlotAfterDelayAsync(slotToDelete);
        }               
    }
}
