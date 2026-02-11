using SubnauticaLauncher.Macros;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace SubnauticaLauncher.Explosion
{
    [SupportedOSPlatform("windows")]
    public static class ExplosionResetService
    {
        private static readonly string ToolsDir =
            Path.Combine(AppContext.BaseDirectory, "tools");

        private static readonly string SignalFile =
            Path.Combine(ToolsDir, "explode.signal");

        private static volatile bool _abortRequested;

        public static void Abort()
        {
            _abortRequested = true;

            try
            {
                if (File.Exists(SignalFile))
                    File.Delete(SignalFile);

                foreach (var p in Process.GetProcessesByName("ExplosionResetHelper2018"))
                    p.Kill();

                foreach (var p in Process.GetProcessesByName("ExplosionResetHelper2022"))
                    p.Kill();
            }
            catch { }

            // 🔴 ALWAYS close overlay on abort
            ExplosionResetDisplayController.Stop("Macro Canceled");
        }

        public static async Task RunAsync(
            GameMode mode,
            ExplosionResetPreset preset,
            CancellationToken token)
        {
            _abortRequested = false;

            var proc = Process.GetProcessesByName("Subnautica").FirstOrDefault();
            if (proc == null)
                return;

            int yearGroup = BuildYearResolver.ResolveGroupedYear(
                Path.GetDirectoryName(proc.MainModule!.FileName!)!);

            var resolver = ExplosionResolverFactory.Get(yearGroup);

            ExplosionResetDisplayController.Start(proc, resolver);

            while (!_abortRequested && !token.IsCancellationRequested)
            {
                // 🔹 SELECT GAMEMODE                
                await Task.Delay(250, token); // ✅ requested delay
                ExplosionResetDisplayController.SetStep($"Selecting \"{mode}\"");
                await ResetMacroService.RunAsync(mode);

                if (_abortRequested || token.IsCancellationRequested)
                    goto CANCELED;

                await Task.Delay(50, token); // ✅ extra input spacing

                // 🔹 WAIT FOR BLACK SCREEN
                ExplosionResetDisplayController.SetStep("Waiting for Loading...");

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 45000)
                {
                    if (_abortRequested || token.IsCancellationRequested)
                        goto CANCELED;

                    if (GameStateDetector.IsBlackScreen(
                        GameStateDetectorRegistry.Get(yearGroup),
                        Display.DisplayInfo.GetPrimary()))
                        break;

                    await Task.Delay(50, token);
                }

                await Task.Delay(2000, token);

                // 🔹 EXPLOSION CHECK (ONLY HERE)
                if (!resolver.TryRead(proc, out var snap))
                    goto CANCELED;

                var (min, max) = ExplosionPresetRanges.Get(preset);

                // ✅ GOOD TIME — DO NOTHING ELSE
                if (snap.ExplosionTime >= min && snap.ExplosionTime <= max)
                {
                    ExplosionResetDisplayController.SetStep("Good Time - Closing Window");

                    ExplosionResetTracker.WriteGood(
                        snap.ExplosionTime,
                        ExplosionResetDisplayController.ResetCount);

                    ExplosionResetDisplayController.Stop(
                        "Good Time - Closing Window",
                        closeDelayMs: 2000); // ✅ requested

                    return; // 🔒 NOTHING AFTER THIS
                }

                // ❌ BAD TIME
                ExplosionResetDisplayController.SetStep("Bad Time - Skipping Cutscene");

                TriggerExplosionHelper(yearGroup);

                while (File.Exists(SignalFile))
                {
                    if (_abortRequested || token.IsCancellationRequested)
                        goto CANCELED;

                    await Task.Delay(50, token);
                }

                ExplosionResetDisplayController.IncrementResetCount();
            }

        CANCELED:
            ExplosionResetTracker.WriteCanceled(
                ExplosionResetDisplayController.ResetCount);

            ExplosionResetDisplayController.Stop("Macro Canceled");
        }

        private static void TriggerExplosionHelper(int yearGroup)
        {
            string exe = Path.Combine(
                ToolsDir,
                yearGroup == 2018
                    ? "ExplosionResetHelper2018.exe"
                    : "ExplosionResetHelper2022.exe");

            if (!File.Exists(exe))
                return;

            if (File.Exists(SignalFile))
                File.Delete(SignalFile);

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = ToolsDir,
                UseShellExecute = true
            });

            File.WriteAllText(SignalFile, "go");
        }
    }
}
