using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Forms = System.Windows.Forms;

namespace SubnauticaLauncher.Services;

internal static class LiveSplitPluginHostMode
{
    public const string Command = "--livesplit-plugin-host";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        HostOptions? options = null;
        try
        {
            options = HostOptions.Parse(args.Skip(1).ToArray());
            Directory.CreateDirectory(options.DataDirectory);
            Directory.CreateDirectory(options.LogsDirectory);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.TrimEndingDirectorySeparator(options.LogsDirectory) + Path.DirectorySeparatorChar);
            AssemblyLoadContext.Default.Resolving += (_, assemblyName) => ResolveAssembly(options, assemblyName);
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            using var context = new PluginApplicationContext(options);
            Forms.Application.Run(context);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(Unwrap(exception));
            return 1;
        }
    }

    private static Assembly? ResolveAssembly(HostOptions options, AssemblyName assemblyName)
    {
        string fileName = assemblyName.Name + ".dll";
        string pluginCandidate = Path.Combine(Path.GetDirectoryName(options.AssemblyPath)!, fileName);
        return File.Exists(pluginCandidate) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(pluginCandidate) : null;
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null }) exception = exception.InnerException;
        return exception;
    }

    private sealed class PluginApplicationContext : Forms.ApplicationContext
    {
        private readonly HostOptions _options;
        private readonly PluginHostForm _hostForm;
        private readonly LiveSplitState _state;
        private readonly IInvalidator _invalidator;
        private readonly IComponent _component;
        private readonly Forms.Timer _updateTimer;
        private DateTime _lastSettingsWriteUtc;
        private DateTime _nextControlCheckUtc;
        private bool _ready;
        private bool _stopping;
        private string? _lastReportedError;

        public PluginApplicationContext(HostOptions options)
        {
            _options = options;
            _hostForm = new PluginHostForm
            {
                ShowInTaskbar = false,
                FormBorderStyle = Forms.FormBorderStyle.None,
                StartPosition = Forms.FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                Size = new Size(1, 1),
                Opacity = 0
            };
            MainForm = _hostForm;
            _hostForm.Show();

            var layoutSettings = new LiveSplit.Options.LayoutSettings();
            var layout = new Layout { Settings = layoutSettings, Mode = LayoutMode.Vertical };
            var run = new Run(new StandardComparisonGeneratorsFactory());
            run.Add(new Segment("Launcher", new Time(), new Time(), null, new Time()));
            var launcherSettings = new Settings();
            _state = new LiveSplitState(run, _hostForm, layout, layoutSettings, launcherSettings);
            var timerModel = new TimerModel { CurrentState = _state };
            _state.RegisterTimerModel(timerModel);
            _invalidator = new Invalidator(_hostForm);
            _component = CreateComponent(options, _state);
            LoadSettings();

            _updateTimer = new Forms.Timer { Interval = 16, Enabled = true };
            _updateTimer.Tick += Update;
        }

        private static IComponent CreateComponent(HostOptions options, LiveSplitState state)
        {
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(options.AssemblyPath);
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }

            var factories = new List<IComponentFactory>();
            foreach (Type type in types.Where(type => !type.IsAbstract && typeof(IComponentFactory).IsAssignableFrom(type)))
            {
                if (Activator.CreateInstance(type, nonPublic: true) is IComponentFactory factory) factories.Add(factory);
            }

            IComponentFactory? selected = factories.FirstOrDefault(factory =>
                string.Equals(factory.ComponentName, options.ComponentName, StringComparison.OrdinalIgnoreCase));
            if (selected is null && factories.Count == 1) selected = factories[0];
            if (selected is null)
            {
                string available = factories.Count == 0
                    ? "none"
                    : string.Join(", ", factories.Select(factory => $"'{factory.ComponentName}'"));
                throw new InvalidDataException(
                    $"The LiveSplit component '{options.ComponentName}' was not found in {Path.GetFileName(options.AssemblyPath)}. Available components: {available}.");
            }

            return selected.Create(state)
                ?? throw new InvalidOperationException($"{selected.ComponentName} returned no component.");
        }

        private void Update(object? sender, EventArgs eventArgs)
        {
            if (_stopping) return;
            try
            {
                DateTime now = DateTime.UtcNow;
                if (now >= _nextControlCheckUtc)
                {
                    _nextControlCheckUtc = now.AddMilliseconds(250);
                    if (ShouldStop()) { Stop(); return; }
                    ReloadSettingsIfChanged();
                }
                _component.Update(_invalidator, _state, 0, 0, LayoutMode.Vertical);
                if (!_ready)
                {
                    _ready = true;
                    Console.Out.WriteLine($"Running|{_component.ComponentName}|{Environment.ProcessId}|{_component.GetType().FullName}");
                    Console.Out.Flush();
                }
                _lastReportedError = null;
            }
            catch (Exception exception)
            {
                string error = Unwrap(exception).ToString();
                if (!string.Equals(error, _lastReportedError, StringComparison.Ordinal))
                {
                    _lastReportedError = error;
                    Console.Error.WriteLine(error);
                }
            }
        }

        private bool ShouldStop()
        {
            if (File.Exists(_options.StopPath)) return true;
            try
            {
                using Process parent = Process.GetProcessById(_options.ParentProcessId);
                return parent.HasExited;
            }
            catch { return true; }
        }

        private void ReloadSettingsIfChanged()
        {
            if (!File.Exists(_options.SettingsPath)) return;
            DateTime writeUtc = File.GetLastWriteTimeUtc(_options.SettingsPath);
            if (writeUtc <= _lastSettingsWriteUtc) return;
            LoadSettings();
        }

        private void LoadSettings()
        {
            if (!File.Exists(_options.SettingsPath)) return;
            var document = new XmlDocument();
            document.Load(_options.SettingsPath);
            if (document.DocumentElement is null) throw new InvalidDataException("The plugin settings XML has no root element.");
            _component.SetSettings(document.DocumentElement);
            _lastSettingsWriteUtc = File.GetLastWriteTimeUtc(_options.SettingsPath);
        }

        private void SaveSettings()
        {
            var document = new XmlDocument();
            document.AppendChild(_component.GetSettings(document));
            string temporary = _options.SettingsPath + ".host.tmp";
            document.Save(temporary);
            File.Move(temporary, _options.SettingsPath, true);
        }

        private void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            _updateTimer.Stop();
            try { SaveSettings(); } catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
            try { _component.Dispose(); } catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
            try { _hostForm.Dispose(); } catch { }
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_stopping) Stop();
                _updateTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private sealed class PluginHostForm : Forms.Form
    {
        protected override bool ShowWithoutActivation => true;

        protected override Forms.CreateParams CreateParams
        {
            get
            {
                const int toolWindow = 0x00000080;
                const int noActivate = 0x08000000;
                Forms.CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= toolWindow | noActivate;
                return parameters;
            }
        }
    }

    private sealed class HostOptions
    {
        public required string AssemblyPath { get; init; }
        public required string ComponentName { get; init; }
        public required string SettingsPath { get; init; }
        public required string DataDirectory { get; init; }
        public required string LogsDirectory { get; init; }
        public required string StopPath { get; init; }
        public required int ParentProcessId { get; init; }

        public static HostOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index + 1 < args.Length; index += 2) values[args[index]] = args[index + 1];
            if (!int.TryParse(Required(values, "--parent"), out int parent) || parent <= 0)
                throw new ArgumentException("The plugin host requires a valid launcher process ID.");

            string assemblyPath = Path.GetFullPath(Required(values, "--assembly"));
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("The plugin assembly is missing.", assemblyPath);
            return new HostOptions
            {
                AssemblyPath = assemblyPath,
                ComponentName = Required(values, "--component"),
                SettingsPath = Path.GetFullPath(Required(values, "--settings")),
                DataDirectory = Path.GetFullPath(Required(values, "--data")),
                LogsDirectory = Path.GetFullPath(Required(values, "--logs")),
                StopPath = Path.GetFullPath(Required(values, "--stop")),
                ParentProcessId = parent
            };
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string key)
        {
            if (!values.TryGetValue(key, out string? value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Missing required argument {key}.");
            return value;
        }
    }
}
