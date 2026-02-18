using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Macros;
using System;
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

        private const ResetMacroLogChannel LogChannel = ResetMacroLogChannel.Explosion;

        public static void Abort()
        {
            _abortRequested = true;
            ResetMacroLogger.Warn(LogChannel, "Abort requested.");

            try
            {
                bool deletedSignal = false;
                if (File.Exists(SignalFile))
                {
                    File.Delete(SignalFile);
                    deletedSignal = true;
                }

                int killedHelpers = 0;
                foreach (Process p in Process.GetProcessesByName("ExplosionResetHelper2018"))
                {
                    p.Kill();
                    killedHelpers++;
                }

                foreach (Process p in Process.GetProcessesByName("ExplosionResetHelper2022"))
                {
                    p.Kill();
                    killedHelpers++;
                }

                ResetMacroLogger.Info(
                    LogChannel,
                    $"Abort cleanup complete. SignalDeleted={deletedSignal}, HelpersKilled={killedHelpers}.");
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(LogChannel, ex, "Abort cleanup failed.");
            }

            ExplosionResetDisplayController.Stop("Macro Canceled");
        }

        public static async Task RunAsync(
            GameMode mode,
            ExplosionResetPreset preset,
            CancellationToken token)
        {
            _abortRequested = false;

            Process[] processes = Process.GetProcessesByName("Subnautica");
            if (processes.Length == 0)
            {
                ResetMacroLogger.Warn(
                    LogChannel,
                    "Explosion reset requested but Subnautica process is not running.");
                return;
            }
            Process process = processes[0];

            int yearGroup = BuildYearResolver.ResolveGroupedYear(
                Path.GetDirectoryName(process.MainModule!.FileName!)!);

            var resolver = ExplosionResolverFactory.Get(yearGroup);
            string resolverName = resolver.GetType().Name;

            ResetMacroLogger.Info(
                LogChannel,
                $"Start explosion reset. Mode={mode}, Preset={preset}, PID={process.Id}, YearGroup={yearGroup}, Resolver={resolverName}.");

            ExplosionResetDisplayController.Start(process, resolver);

            bool completedWithGoodTime = false;
            bool canceled = false;

            try
            {
                int cycle = 0;

                while (!_abortRequested && !token.IsCancellationRequested)
                {
                    cycle++;
                    ResetMacroLogger.Info(LogChannel, $"Cycle {cycle}: begin.");

                    await Task.Delay(250, token);
                    ExplosionResetDisplayController.SetStep($"Selecting \"{mode}\"");
                    await ResetMacroService.RunAsync(mode, ResetMacroLogChannel.Explosion);

                    if (_abortRequested || token.IsCancellationRequested)
                    {
                        canceled = true;
                        break;
                    }

                    await Task.Delay(50, token);

                    ExplosionResetDisplayController.SetStep("Waiting for Loading...");

                    bool sawBlackScreen = false;
                    var loadWait = Stopwatch.StartNew();
                    while (loadWait.ElapsedMilliseconds < 45000)
                    {
                        if (_abortRequested || token.IsCancellationRequested)
                        {
                            canceled = true;
                            break;
                        }

                        if (GameStateDetector.IsBlackScreen(
                            GameStateDetectorRegistry.Get(yearGroup),
                            Display.DisplayInfo.GetPrimary()))
                        {
                            sawBlackScreen = true;
                            break;
                        }

                        await Task.Delay(50, token);
                    }

                    if (canceled)
                        break;

                    if (!sawBlackScreen)
                    {
                        ResetMacroLogger.Warn(
                            LogChannel,
                            $"Cycle {cycle}: loading black screen was not detected within {loadWait.ElapsedMilliseconds}ms.");
                    }
                    else
                    {
                        ResetMacroLogger.Info(
                            LogChannel,
                            $"Cycle {cycle}: loading black screen detected after {loadWait.ElapsedMilliseconds}ms.");
                    }

                    await Task.Delay(2000, token);

                    if (!resolver.TryRead(process, out var snapshot))
                    {
                        ResetMacroLogger.Warn(
                            LogChannel,
                            $"Cycle {cycle}: unable to read explosion timer snapshot from process.");
                        break;
                    }

                    var (min, max) = ExplosionPresetRanges.Get(preset);
                    ResetMacroLogger.Info(
                        LogChannel,
                        $"Cycle {cycle}: explosion={snapshot.ExplosionTime:F3}s, target={min:F3}s..{max:F3}s.");

                    if (snapshot.ExplosionTime >= min && snapshot.ExplosionTime <= max)
                    {
                        ExplosionResetDisplayController.SetStep("Good Time - Closing Window");
                        ExplosionResetTracker.WriteGood(
                            snapshot.ExplosionTime,
                            ExplosionResetDisplayController.ResetCount);
                        ExplosionResetDisplayController.Stop(
                            "Good Time - Closing Window",
                            closeDelayMs: 2000);

                        completedWithGoodTime = true;
                        ResetMacroLogger.Info(
                            LogChannel,
                            $"Cycle {cycle}: good time found. ResetCount={ExplosionResetDisplayController.ResetCount}.");
                        return;
                    }

                    ExplosionResetDisplayController.SetStep("Bad Time - Skipping Cutscene");

                    if (!TriggerExplosionHelper(yearGroup))
                    {
                        ResetMacroLogger.Error(
                            LogChannel,
                            $"Cycle {cycle}: failed to start explosion helper.");
                        break;
                    }

                    var helperWait = Stopwatch.StartNew();
                    while (File.Exists(SignalFile))
                    {
                        if (_abortRequested || token.IsCancellationRequested)
                        {
                            canceled = true;
                            break;
                        }

                        await Task.Delay(50, token);
                    }

                    if (canceled)
                        break;

                    ExplosionResetDisplayController.IncrementResetCount();
                    ResetMacroLogger.Info(
                        LogChannel,
                        $"Cycle {cycle}: helper completed in {helperWait.ElapsedMilliseconds}ms, reset count incremented.");
                }
            }
            catch (OperationCanceledException)
            {
                canceled = true;
                ResetMacroLogger.Warn(LogChannel, "Explosion reset canceled by cancellation token.");
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(LogChannel, ex, "Explosion reset failed.");
                throw;
            }
            finally
            {
                if (!completedWithGoodTime)
                {
                    ExplosionResetTracker.WriteCanceled(
                        ExplosionResetDisplayController.ResetCount);
                    ExplosionResetDisplayController.Stop("Macro Canceled");

                    string reason = canceled
                        ? "canceled"
                        : "stopped before finding a good time";
                    ResetMacroLogger.Warn(
                        LogChannel,
                        $"Explosion reset ended: {reason}. ResetCount={ExplosionResetDisplayController.ResetCount}.");
                }
            }
        }

        private static bool TriggerExplosionHelper(int yearGroup)
        {
            string exe = Path.Combine(
                ToolsDir,
                yearGroup == 2018
                    ? "ExplosionResetHelper2018.exe"
                    : "ExplosionResetHelper2022.exe");

            if (!File.Exists(exe))
            {
                ResetMacroLogger.Error(
                    LogChannel,
                    $"Explosion helper missing: {exe}");
                return false;
            }

            try
            {
                if (File.Exists(SignalFile))
                    File.Delete(SignalFile);

                Process? started = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = ToolsDir,
                    UseShellExecute = true
                });

                if (started == null)
                {
                    ResetMacroLogger.Error(
                        LogChannel,
                        $"Failed to start explosion helper: {exe}");
                    return false;
                }

                File.WriteAllText(SignalFile, "go");
                ResetMacroLogger.Info(
                    LogChannel,
                    $"Started helper {Path.GetFileName(exe)} (PID={started.Id}).");
                return true;
            }
            catch (Exception ex)
            {
                ResetMacroLogger.Exception(LogChannel, ex, "TriggerExplosionHelper failed.");
                return false;
            }
        }
    }
}
