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

        private static volatile bool _abortRequested;

        private const ResetMacroLogChannel LogChannel = ResetMacroLogChannel.Explosion;

        public static void Abort()
        {
            _abortRequested = true;
            ResetMacroLogger.Warn(LogChannel, "Abort requested.");

            try
            {
                NativeInput.StopHoldingAllKeys();

                ResetMacroLogger.Info(
                    LogChannel,
                    "Abort cleanup complete. Native input holds released.");
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

                    var helperWait = Stopwatch.StartNew();
                    int holdDurationMs = GetCutsceneSkipDurationMs(yearGroup);

                    try
                    {
                        await NativeInput.HoldEscAsync(process, holdDurationMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        canceled = true;
                        break;
                    }

                    ExplosionResetDisplayController.IncrementResetCount();
                    ResetMacroLogger.Info(
                        LogChannel,
                        $"Cycle {cycle}: native hold skip completed in {helperWait.ElapsedMilliseconds}ms, hold={holdDurationMs}ms, reset count incremented.");
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

        private static int GetCutsceneSkipDurationMs(int yearGroup)
        {
            return yearGroup == 2018 ? 1050 : 1550;
        }
    }
}
