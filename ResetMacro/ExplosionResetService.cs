using SubnauticaLauncher.Macros;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using UpdatesData = SubnauticaLauncher.Updates.Updates;

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

            string root = Path.GetDirectoryName(proc.MainModule!.FileName!)!;
            int yearGroup = BuildYearResolver.ResolveGroupedYear(root);

            bool is2018 = yearGroup == 2018;
            bool is2022Plus = yearGroup >= 2022;
            if (!is2018 && !is2022Plus)
                return;

            string ahkExe = Path.Combine(
                ToolsDir,
                is2018 ? "ExplosionResetHelper2018.exe"
                       : "ExplosionResetHelper2022.exe"
            );

            if (!File.Exists(ahkExe))
            {
                Logger.Warn($"Explosion helper missing: {ahkExe}");
                return;
            }

            var resolver = ExplosionResolverFactory.Get(yearGroup);
            var display = Display.DisplayInfo.GetPrimary();
            var profile = GameStateDetectorRegistry.Get(yearGroup);

            while (!_abortRequested && !token.IsCancellationRequested)
            {
                await ResetMacroService.RunAsync(mode);

                // wait for black screen
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 15000)
                {
                                       
                    if (_abortRequested || token.IsCancellationRequested)
                        return;

                    if (GameStateDetector.IsBlackScreen(profile, display))
                        break;

                    await Task.Delay(50, token);
                }

                await Task.Delay(1500, token);

                if (!resolver.TryRead(proc, out var snap))
                    return;
                
                var (min, max) = ExplosionPresetRanges.Get(preset);

                if (snap.ExplosionTime >= min && snap.ExplosionTime <= max)
                {
                    Logger.Log("[ExplosionReset] GOOD RUN");
                    Abort(); // ensure helpers closed
                                     
                    return;
                }

                // BAD RUN → skip cutscene
                if (File.Exists(SignalFile))
                    File.Delete(SignalFile);
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = ahkExe,
                    WorkingDirectory = ToolsDir,
                    UseShellExecute = true
                });

                await Task.Delay(100, token);
                File.WriteAllText(SignalFile, "go");

                // wait helper
                var wait = Stopwatch.StartNew();
                while (File.Exists(SignalFile) && wait.ElapsedMilliseconds < 5000)
                {
                    if (_abortRequested || token.IsCancellationRequested)
                        return;

                    await Task.Delay(50, token);
                }

                await Task.Delay(250, token);
                await ResetMacroService.RunAsync(mode);
            }
        }
    }
}