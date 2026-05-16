using SubnauticaLauncher.Enums;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace SubnauticaLauncher.Macros;

internal readonly record struct Subnautica2LogSnapshot(
    bool IsReady,
    string LogPath,
    long LogPosition,
    GameState State,
    bool IsCreativeMode,
    bool CharacterSelectOpen,
    bool CharacterSelectConfirmPulse,
    bool MainMenuResetPulse,
    bool MainMenuReadyPulse,
    bool IsMainMenuReady,
    bool SurvivalStartPulse)
{
    public bool HasKnownState => State is GameState.MainMenu or GameState.InGame;
}

internal readonly record struct Subnautica2LogLineContext(
    DateTime? TimestampUtc,
    bool IsCurrentSessionLine);

internal sealed class Subnautica2LogStateReader
{
    private const int ActiveLogStaleFrameThreshold = 90;
    private const int TailBootstrapByteCount = 256 * 1024;

    public static Subnautica2LogStateReader Shared { get; } = new();

    private readonly object _sync = new();
    private readonly string _logsDirectory;
    private string _logPath;
    private long _logPosition;
    private int _staleFrames;
    private bool _initialized;
    private bool _hasKnownState;
    private bool _isInMainMenu;
    private bool _isCreativeMode;
    private bool _characterSelectOpen;
    private bool _characterSelectConfirmPulse;
    private bool _mainMenuResetPulse;
    private bool _mainMenuReadyPulse;
    private bool _isMainMenuReady;
    private bool _survivalStartPulse;

    private Subnautica2LogStateReader()
    {
        _logsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Subnautica2",
            "Saved",
            "Logs");
        _logPath = ResolveActiveLogPath();
    }

    public Subnautica2LogSnapshot Update(DateTime? processStartUtc = null)
    {
        lock (_sync)
        {
            EnsureActiveLogPath();

            if (!File.Exists(_logPath))
            {
                _initialized = false;
                return CreateSnapshot(isReady: false);
            }

            if (!_initialized)
            {
                BootstrapCurrentStateFromTail(processStartUtc);
                _logPosition = SafeGetFileLength(_logPath);
                _initialized = true;
                ResetPulses();
                return CreateSnapshot(isReady: true);
            }

            ResetPulses();

            try
            {
                long fileLength = SafeGetFileLength(_logPath);
                if (fileLength < _logPosition)
                    _logPosition = 0L;

                int linesRead = 0;
                using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                fs.Seek(_logPosition, SeekOrigin.Begin);

                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    linesRead++;
                    HandleLine(
                        line,
                        emitPulses: true,
                        context: CreateLineContext(line, processStartUtc, requireCurrentSession: false));
                }

                _logPosition = fs.Position;

                if (linesRead > 0)
                {
                    _staleFrames = 0;
                }
                else
                {
                    _staleFrames++;
                    if (_staleFrames > ActiveLogStaleFrameThreshold)
                        TrySwitchToNewerActiveLog();
                }
            }
            catch
            {
                return CreateSnapshot(isReady: false);
            }

            return CreateSnapshot(isReady: true);
        }
    }

    private void EnsureActiveLogPath()
    {
        if (!File.Exists(_logPath))
        {
            SwitchToLog(ResolveActiveLogPath());
            return;
        }

        string resolved = ResolveActiveLogPath();
        if (!string.Equals(resolved, _logPath, StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
            SwitchToLog(resolved);
    }

    private void TrySwitchToNewerActiveLog()
    {
        string resolved = ResolveActiveLogPath();
        if (!string.Equals(resolved, _logPath, StringComparison.OrdinalIgnoreCase) && File.Exists(resolved))
            SwitchToLog(resolved);
    }

    private void SwitchToLog(string newPath)
    {
        _logPath = newPath;
        _staleFrames = 0;
        _initialized = false;
    }

    private string ResolveActiveLogPath()
    {
        string fallback = Path.Combine(_logsDirectory, "Subnautica2.log");
        if (!Directory.Exists(_logsDirectory))
            return fallback;

        FileInfo? candidate = Directory
            .EnumerateFiles(_logsDirectory, "Subnautica2*.log", SearchOption.TopDirectoryOnly)
            .Where(path => path.IndexOf("backup", StringComparison.OrdinalIgnoreCase) < 0)
            .Select(path => new FileInfo(path))
            .Where(info => info.Exists)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ThenByDescending(info => info.Length)
            .FirstOrDefault();

        return candidate?.FullName ?? fallback;
    }

    private void BootstrapCurrentStateFromTail(DateTime? processStartUtc)
    {
        _hasKnownState = false;
        _isInMainMenu = false;
        _isCreativeMode = false;
        _characterSelectOpen = false;
        _isMainMenuReady = false;

        try
        {
            long fileLength = SafeGetFileLength(_logPath);
            if (fileLength <= 0)
                return;

            int bytesToRead = (int)Math.Min(fileLength, TailBootstrapByteCount);
            using var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            fs.Seek(fileLength - bytesToRead, SeekOrigin.Begin);

            byte[] buffer = new byte[bytesToRead];
            int read = fs.Read(buffer, 0, bytesToRead);
            if (read <= 0)
                return;

            string text = Encoding.UTF8.GetString(buffer, 0, read);
            foreach (string line in text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                HandleLine(
                    line,
                    emitPulses: false,
                    context: CreateLineContext(line, processStartUtc, requireCurrentSession: true));
            }
        }
        catch
        {
            _hasKnownState = false;
        }
    }

    private void HandleLine(
        string line,
        bool emitPulses,
        Subnautica2LogLineContext context)
    {
        if (!context.IsCurrentSessionLine)
            return;

        if (line.IndexOf("Browse Started Browse:", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("/Game/Maps/L_ClientLobby", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _hasKnownState = true;
            _isInMainMenu = true;
            _isCreativeMode = false;
            _characterSelectOpen = false;
            _isMainMenuReady = false;

            if (emitPulses &&
                line.IndexOf("MenuReturnReason=Quit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _mainMenuResetPulse = true;
            }
        }

        if (line.IndexOf("Browse Started Browse:", StringComparison.OrdinalIgnoreCase) >= 0 &&
            line.IndexOf("/Game/Maps/Main/L_Main", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _hasKnownState = true;
            _isInMainMenu = false;
            _characterSelectOpen = false;
            _isMainMenuReady = false;
            _isCreativeMode =
                line.IndexOf("game=Creative", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        if (line.IndexOf("PushToLayer: Layer 5 Widget WBP_CharacterSelectScreen", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _characterSelectOpen = true;
        }

        if (line.IndexOf("Applying input config for leaf-most node [WBP_MainLobbyScreen", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("Focused desired target PlaySinglePlayerButton", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("SetWidgetOnLayer: Layer 3 Widget WBP_MainLobbyScreen_C", StringComparison.OrdinalIgnoreCase) >= 0 ||
            line.IndexOf("FrontendState=2", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (!_isMainMenuReady && emitPulses)
                _mainMenuReadyPulse = true;

            _isMainMenuReady = true;
        }

        if (line.IndexOf("Pop: Layer 5 Widget WBP_CharacterSelectScreen", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            if (emitPulses && _characterSelectOpen)
                _characterSelectConfirmPulse = true;

            _characterSelectOpen = false;
        }

        if (emitPulses &&
            line.IndexOf("UUWEFirstPersonCamera::EndCinematicLocation", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            _survivalStartPulse = true;
        }
    }

    private static Subnautica2LogLineContext CreateLineContext(
        string line,
        DateTime? processStartUtc,
        bool requireCurrentSession)
    {
        DateTime? timestampUtc = TryParseTimestampUtc(line);
        bool isCurrentSessionLine = !requireCurrentSession;

        if (processStartUtc.HasValue)
        {
            if (timestampUtc.HasValue)
            {
                isCurrentSessionLine = timestampUtc.Value >= processStartUtc.Value.AddSeconds(-2);
            }
            else
            {
                isCurrentSessionLine = !requireCurrentSession;
            }
        }
        else
        {
            isCurrentSessionLine = !requireCurrentSession;
        }

        return new Subnautica2LogLineContext(timestampUtc, isCurrentSessionLine);
    }

    private static DateTime? TryParseTimestampUtc(string line)
    {
        int start = line.IndexOf('[');
        if (start < 0 || start + 25 >= line.Length)
            return null;

        int end = line.IndexOf(']', start + 1);
        if (end < 0)
            return null;

        ReadOnlySpan<char> token = line.AsSpan(start + 1, end - start - 1);
        if (token.Length < 23)
            return null;

        string trimmed = token[..23].ToString();
        if (!DateTime.TryParseExact(
                trimmed,
                "yyyy.MM.dd-HH.mm.ss:fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out DateTime parsed))
        {
            return null;
        }

        return parsed;
    }

    private void ResetPulses()
    {
        _characterSelectConfirmPulse = false;
        _mainMenuResetPulse = false;
        _mainMenuReadyPulse = false;
        _survivalStartPulse = false;
    }

    private Subnautica2LogSnapshot CreateSnapshot(bool isReady)
    {
        GameState state = !_hasKnownState
            ? GameState.Unknown
            : _isInMainMenu
                ? GameState.MainMenu
                : GameState.InGame;

        return new Subnautica2LogSnapshot(
            isReady,
            _logPath,
            _logPosition,
            state,
            _isCreativeMode,
            _characterSelectOpen,
            _characterSelectConfirmPulse,
            _mainMenuResetPulse,
            _mainMenuReadyPulse,
            _isMainMenuReady,
            _survivalStartPulse);
    }

    private static long SafeGetFileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0L;
        }
    }
}
