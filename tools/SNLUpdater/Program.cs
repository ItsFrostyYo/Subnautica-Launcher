using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    private const int MaxReplaceAttempts = 50;
    private const int ReplaceDelayMs = 200;

    private static string LogPath = string.Empty;
    private static UpdateStatusWindow? _statusWindow;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ApplicationConfiguration.Initialize();

            if (args.Length < 2)
                return Fail("Usage: SNLUpdater.exe <newExePath> <targetExePath> [launcherPid]");

            string newExePath = Path.GetFullPath(args[0]);
            string targetExePath = Path.GetFullPath(args[1]);
            int? launcherPid = TryParsePid(args);

            string appDir = Path.GetDirectoryName(targetExePath)
                ?? AppContext.BaseDirectory;

            Directory.CreateDirectory(appDir);

            string logsDir = Path.Combine(appDir, "logs");
            Directory.CreateDirectory(logsDir);
            LogPath = Path.Combine(logsDir, "updater.log");

            Log("Updater started");
            Log($"New exe: {newExePath}");
            Log($"Target exe: {targetExePath}");

            if (!File.Exists(newExePath))
                return Fail("Downloaded launcher executable is missing.");

            WaitForLauncherToExit(launcherPid, targetExePath);

            _statusWindow = new UpdateStatusWindow();
            _statusWindow.SetDetectedUpdate("Applying launcher update");
            _statusWindow.SetStatus("Launcher closed");
            _statusWindow.SetIndeterminate("Continuing update...");
            _statusWindow.Show();
            PumpUi();

            string stagedPath = Path.Combine(appDir, "SubnauticaLauncher.staged.exe");
            string backupPath = Path.Combine(appDir, "SubnauticaLauncher.previous.exe");

            SetUiStep("Staging updated launcher...", "Copying new files...", indeterminate: true);
            CleanupFile(stagedPath);
            CleanupFile(backupPath);

            File.Copy(newExePath, stagedPath, overwrite: true);
            if (File.Exists(targetExePath))
            {
                SetUiStep("Replacing launcher files...", "Updating launcher executable...", indeterminate: true);
                ReplaceWithRetry(stagedPath, targetExePath, backupPath);
                CleanupFile(backupPath);
            }
            else
            {
                File.Move(stagedPath, targetExePath, overwrite: true);
            }

            SetUiStep("Cleaning temporary files...", "Removing staged files...", indeterminate: true);
            CleanupFile(stagedPath);
            CleanupFile(newExePath);

            SetUiStep("Relaunching launcher...", "Opening updated launcher...", percent: 100);
            StartUpdatedLauncher(targetExePath);
            Log("Updater completed successfully");

            Thread.Sleep(500);
            return 0;
        }
        catch (Exception ex)
        {
            return Fail("Updater failed: " + ex);
        }
        finally
        {
            try
            {
                _statusWindow?.Close();
                _statusWindow?.Dispose();
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static int? TryParsePid(string[] args)
    {
        if (args.Length < 3)
            return null;

        return int.TryParse(args[2], out int pid) && pid > 0 ? pid : null;
    }

    private static void WaitForLauncherToExit(int? launcherPid, string targetExePath)
    {
        if (launcherPid.HasValue)
        {
            try
            {
                using Process launcher = Process.GetProcessById(launcherPid.Value);
                Log($"Waiting for launcher PID {launcherPid.Value} to exit...");

                if (!launcher.WaitForExit(15000))
                    Log("Launcher PID did not exit in 15s, continuing with retry-based replacement.");
            }
            catch (ArgumentException)
            {
                Log("Launcher PID not running, continuing.");
            }
            catch (Exception ex)
            {
                Log("Failed waiting by PID: " + ex.Message);
            }
        }

        Thread.Sleep(250);

        for (int i = 0; i < 40; i++)
        {
            bool stillRunning = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(targetExePath))
                .Any(p =>
                {
                    try
                    {
                        return string.Equals(
                            p.MainModule?.FileName,
                            targetExePath,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                });

            if (!stillRunning)
                return;

            Thread.Sleep(200);
            PumpUi();
        }
    }

    private static void ReplaceWithRetry(string stagedPath, string targetExePath, string backupPath)
    {
        for (int attempt = 1; attempt <= MaxReplaceAttempts; attempt++)
        {
            try
            {
                File.Replace(stagedPath, targetExePath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException ex)
            {
                Log($"Replace attempt {attempt}/{MaxReplaceAttempts} failed: {ex.Message}");
                SetUiStep(
                    "Replacing launcher files...",
                    $"Retrying file replacement ({attempt}/{MaxReplaceAttempts})...",
                    indeterminate: true);
                Thread.Sleep(ReplaceDelayMs);
                PumpUi();
            }
            catch (UnauthorizedAccessException ex)
            {
                Log($"Replace attempt {attempt}/{MaxReplaceAttempts} denied: {ex.Message}");
                SetUiStep(
                    "Replacing launcher files...",
                    $"Retrying file replacement ({attempt}/{MaxReplaceAttempts})...",
                    indeterminate: true);
                Thread.Sleep(ReplaceDelayMs);
                PumpUi();
            }
        }

        throw new IOException("Failed to replace launcher executable after multiple retries.");
    }

    private static void StartUpdatedLauncher(string targetExePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = targetExePath,
            WorkingDirectory = Path.GetDirectoryName(targetExePath) ?? AppContext.BaseDirectory,
            UseShellExecute = true
        };

        Process.Start(psi);
    }

    private static void CleanupFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log($"Cleanup failed for {path}: {ex.Message}");
        }
    }

    private static int Fail(string message)
    {
        Log(message);
        try
        {
            SetUiStep("Update failed", message, indeterminate: false, percent: 100);
            Thread.Sleep(1800);
        }
        catch
        {
            // Ignore UI failures here.
        }

        return 1;
    }

    private static void SetUiStep(string status, string detail, bool indeterminate = true, double? percent = null)
    {
        _statusWindow?.SetStatus(status);
        if (indeterminate)
            _statusWindow?.SetIndeterminate(detail);
        else if (percent.HasValue)
            _statusWindow?.SetProgress(percent.Value, detail);
        else
            _statusWindow?.SetIndeterminate(detail);

        PumpUi();
    }

    private static void PumpUi()
    {
        Application.DoEvents();
    }

    private static void Log(string message)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LogPath))
                return;

            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            File.AppendAllText(LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Updater logging must never crash updater flow.
        }
    }
}

internal sealed class UpdateStatusWindow : Form
{
    private readonly Label _titleLabel;
    private readonly Label _detectedUpdateLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _detailLabel;

    public UpdateStatusWindow()
    {
        Text = "Updating Subnautica Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        ClientSize = new Size(520, 230);
        BackColor = Color.FromArgb(10, 37, 55);

        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(238, 10, 37, 55)
        };

        _titleLabel = new Label
        {
            AutoSize = false,
            Text = "Subnautica Launcher Update",
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            Location = new Point(18, 18),
            Size = new Size(420, 28)
        };

        _detectedUpdateLabel = new Label
        {
            AutoSize = false,
            Text = "Checking for updates...",
            ForeColor = Color.FromArgb(216, 240, 255),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            Location = new Point(18, 58),
            Size = new Size(470, 24)
        };

        _statusLabel = new Label
        {
            AutoSize = false,
            Text = "Preparing...",
            ForeColor = Color.FromArgb(255, 231, 192, 119),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            Location = new Point(18, 88),
            Size = new Size(470, 22)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(18, 126),
            Size = new Size(470, 14),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 24
        };

        _detailLabel = new Label
        {
            AutoSize = false,
            Text = "Starting...",
            ForeColor = Color.FromArgb(199, 220, 235),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            Location = new Point(18, 154),
            Size = new Size(470, 24)
        };

        outer.Controls.Add(_titleLabel);
        outer.Controls.Add(_detectedUpdateLabel);
        outer.Controls.Add(_statusLabel);
        outer.Controls.Add(_progressBar);
        outer.Controls.Add(_detailLabel);
        Controls.Add(outer);
    }

    public void SetDetectedUpdate(string text)
    {
        _detectedUpdateLabel.Text = text;
    }

    public void SetStatus(string text)
    {
        _statusLabel.Text = text;
    }

    public void SetIndeterminate(string detail)
    {
        _progressBar.Style = ProgressBarStyle.Marquee;
        _progressBar.MarqueeAnimationSpeed = 24;
        _detailLabel.Text = detail;
    }

    public void SetProgress(double percent, string detail)
    {
        int safePercent = (int)Math.Clamp(percent, 0, 100);
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.MarqueeAnimationSpeed = 0;
        _progressBar.Value = safePercent;
        _detailLabel.Text = detail;
    }
}
