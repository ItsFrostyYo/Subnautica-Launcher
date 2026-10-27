using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Xml;
using System.IO;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Forms = System.Windows.Forms;

namespace SubnauticaLauncher.Services;

internal static class PluginHostMode
{
    public const string Command = "--plugin-host";

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
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
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
        finally
        {
            if (options is not null) AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
        }

        Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (options is null) return null;
            foreach (string directory in options.Components.Select(component => Path.GetDirectoryName(component.AssemblyPath)!).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
            }
            return null;
        }
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
        private readonly Invalidator _invalidator;
        private readonly List<HostedInstance> _instances = [];
        private readonly ComponentRenderer? _renderer;
        private readonly Forms.Timer _updateTimer;
        private readonly Dictionary<string, Forms.Form> _settingsForms = new(StringComparer.OrdinalIgnoreCase);
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

            // Plugin components expect the fully initialized standard layout settings.
            // A bare LayoutSettings leaves fonts and colors null/default and causes otherwise
            // valid visual components to fail during their first Draw call.
            LiveSplit.Options.LayoutSettings layoutSettings = new StandardLayoutSettingsFactory().Create();
            var layout = new Layout { Settings = layoutSettings, Mode = LayoutMode.Vertical };
            var run = new Run(new StandardComparisonGeneratorsFactory());
            run.Add(new Segment("Launcher", new Time(), new Time(), null, new Time()));
            _state = new LiveSplitState(run, _hostForm, layout, layoutSettings, new Settings());
            _state.RegisterTimerModel(new TimerModel { CurrentState = _state });
            _invalidator = new Invalidator(_hostForm);

            foreach (PluginHostedComponent definition in options.Components.OrderBy(component => component.LayoutOrder))
            {
                IComponent component = CreateComponent(definition, _state);
                var instance = new HostedInstance(definition, component);
                LoadSettings(instance);
                _instances.Add(instance);
            }

            IComponent[] components = _instances.Select(instance => instance.Component).ToArray();
            layout.LayoutComponents = _instances
                .Select(instance => (ILayoutComponent)new LayoutComponent(instance.Definition.AssemblyPath, instance.Component))
                .ToList();
            if (components.Any(HasVisualLayout))
            {
                // This mirrors LiveSplit: every layout component is passed to the renderer.
                // Logic components are valid zero-sized entries and are handled generically.
                _renderer = new ComponentRenderer { VisibleComponents = components };
                _renderer.CalculateOverallSize(LayoutMode.Vertical);
                _hostForm.EnableVisual(
                    options.Title,
                    _renderer,
                    _state,
                    layoutSettings,
                    options.DataDirectory,
                    _instances.Select(instance => new HostedComponentContext(
                        instance.Definition.Id,
                        instance.Component)).ToArray(),
                    OpenSettings,
                    RequestDisable);
            }

            _updateTimer = new Forms.Timer { Interval = 16, Enabled = true };
            _updateTimer.Tick += Update;
        }

        private static IComponent CreateComponent(PluginHostedComponent definition, LiveSplitState state)
        {
            Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(definition.AssemblyPath);
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }
            List<IComponentFactory> factories = types
                .Where(type => !type.IsAbstract && typeof(IComponentFactory).IsAssignableFrom(type))
                .Select(type => Activator.CreateInstance(type, nonPublic: true))
                .OfType<IComponentFactory>()
                .ToList();
            IComponentFactory? selected = factories.FirstOrDefault(factory =>
                string.Equals(factory.ComponentName, definition.ComponentName, StringComparison.OrdinalIgnoreCase));
            if (selected is null && factories.Count == 1) selected = factories[0];
            if (selected is null)
            {
                string available = factories.Count == 0 ? "none" : string.Join(", ", factories.Select(factory => $"'{factory.ComponentName}'"));
                throw new InvalidDataException($"The Plugin component '{definition.ComponentName}' was not found in {Path.GetFileName(definition.AssemblyPath)}. Available components: {available}.");
            }
            return selected.Create(state) ?? throw new InvalidOperationException($"{selected.ComponentName} returned no component.");
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
                    foreach (HostedInstance instance in _instances) ReloadSettingsIfChanged(instance);
                }

                if (_renderer is null)
                {
                    foreach (HostedInstance instance in _instances)
                        instance.Component.Update(_invalidator, _state, 0, 0, LayoutMode.Vertical);
                }
                else
                {
                    _renderer.CalculateOverallSize(LayoutMode.Vertical);
                    _hostForm.SetOverallSize(_renderer.OverallSize, _renderer.MinimumHeight);
                    Size size = _hostForm.RenderSize;
                    _invalidator.Restart();
                    _renderer.Update(_invalidator, _state, size.Width, size.Height, LayoutMode.Vertical);
                    _hostForm.InvalidateRenderSurface();
                }

                if (!_ready)
                {
                    _ready = true;
                    string visualIds = string.Join(",", _instances
                        .Where(instance => HasVisualLayout(instance.Component))
                        .Select(instance => instance.Definition.Id));
                    Console.Out.WriteLine($"Running|{string.Join(",", _instances.Select(instance => instance.Definition.Id))}|{Environment.ProcessId}|{visualIds}");
                    Console.Out.Flush();
                }
                _lastReportedError = null;
            }
            catch (Exception exception)
            {
                string error = Unwrap(exception).ToString();
                if (string.Equals(error, _lastReportedError, StringComparison.Ordinal)) return;
                _lastReportedError = error;
                Console.Error.WriteLine(error);
            }
        }

        private static bool HasVisualLayout(IComponent component) =>
            component.VerticalHeight > 0 || component.HorizontalWidth > 0 || component.MinimumWidth > 0 || component.MinimumHeight > 0;

        private bool ShouldStop()
        {
            if (File.Exists(_options.StopPath)) return true;
            try { using Process parent = Process.GetProcessById(_options.ParentProcessId); return parent.HasExited; }
            catch { return true; }
        }

        private void OpenSettings(string componentId)
        {
            if (_settingsForms.TryGetValue(componentId, out Forms.Form? existing) && !existing.IsDisposed)
            {
                existing.Activate();
                return;
            }

            HostedInstance instance = _instances.First(item =>
                string.Equals(item.Definition.Id, componentId, StringComparison.OrdinalIgnoreCase));
            Forms.Form? settingsForm = null;
            settingsForm = PluginSettingsHostMode.ShowForHostedComponent(
                instance.Component,
                instance.Definition.SettingsPath,
                instance.Component.ComponentName + " Settings",
                _hostForm,
                _options.ParentProcessId,
                saved =>
                {
                    if (saved && File.Exists(instance.Definition.SettingsPath))
                        instance.LastSettingsWriteUtc = File.GetLastWriteTimeUtc(instance.Definition.SettingsPath);
                    _settingsForms.Remove(componentId);
                    _hostForm.InvalidateRenderSurface();
                });
            _settingsForms[componentId] = settingsForm;
        }

        private void RequestDisable(string componentId)
        {
            string requestsDirectory = Path.Combine(_options.DataDirectory, "Requests");
            Directory.CreateDirectory(requestsDirectory);
            string safeId = string.Concat(componentId.Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
            string destination = Path.Combine(requestsDirectory, $"disable-{safeId}.request");
            string temporary = destination + ".tmp";
            File.WriteAllText(temporary, componentId);
            File.Move(temporary, destination, true);
        }

        private static void ReloadSettingsIfChanged(HostedInstance instance)
        {
            if (!File.Exists(instance.Definition.SettingsPath)) return;
            DateTime writeUtc = File.GetLastWriteTimeUtc(instance.Definition.SettingsPath);
            if (writeUtc <= instance.LastSettingsWriteUtc) return;
            LoadSettings(instance);
        }

        private static void LoadSettings(HostedInstance instance)
        {
            if (!File.Exists(instance.Definition.SettingsPath)) return;
            var document = new XmlDocument();
            document.Load(instance.Definition.SettingsPath);
            if (document.DocumentElement is null) throw new InvalidDataException($"{instance.Component.ComponentName} settings XML has no root element.");
            instance.Component.SetSettings(document.DocumentElement);
            instance.LastSettingsWriteUtc = File.GetLastWriteTimeUtc(instance.Definition.SettingsPath);
        }

        private static void SaveSettings(HostedInstance instance)
        {
            var document = new XmlDocument();
            document.AppendChild(instance.Component.GetSettings(document));
            Directory.CreateDirectory(Path.GetDirectoryName(instance.Definition.SettingsPath)!);
            string temporary = instance.Definition.SettingsPath + ".host.tmp";
            document.Save(temporary);
            File.Move(temporary, instance.Definition.SettingsPath, true);
        }

        private void Stop()
        {
            if (_stopping) return;
            _stopping = true;
            _updateTimer.Stop();
            foreach (Forms.Form settingsForm in _settingsForms.Values.ToArray())
                try { settingsForm.Close(); } catch { }
            _settingsForms.Clear();
            foreach (HostedInstance instance in _instances)
                try { SaveSettings(instance); } catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
            foreach (HostedInstance instance in _instances.AsEnumerable().Reverse())
                try { instance.Component.Dispose(); } catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
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

        private sealed class HostedInstance(PluginHostedComponent definition, IComponent component)
        {
            public PluginHostedComponent Definition { get; } = definition;
            public IComponent Component { get; } = component;
            public DateTime LastSettingsWriteUtc { get; set; }
        }
    }

    private sealed record HostedComponentContext(string Id, IComponent Component);

    private sealed class PluginHostForm : Forms.Form
    {
        private ComponentRenderer? _renderer;
        private LiveSplitState? _state;
        private LiveSplit.Options.LayoutSettings? _layoutSettings;
        private IReadOnlyList<HostedComponentContext> _components = [];
        private Action<string>? _openSettings;
        private Action<string>? _disablePlugin;
        private readonly Forms.Timer _saveTimer;
        private string? _windowStatePath;
        private int _lastNaturalHeight;
        private bool _visualEnabled;
        private bool _restoringWindow;

        public PluginHostForm()
        {
            SetStyle(
                Forms.ControlStyles.AllPaintingInWmPaint |
                Forms.ControlStyles.OptimizedDoubleBuffer |
                Forms.ControlStyles.ResizeRedraw |
                Forms.ControlStyles.SupportsTransparentBackColor |
                Forms.ControlStyles.UserPaint,
                true);
            MouseDown += DragWindow;
            _saveTimer = new Forms.Timer { Interval = 250 };
            _saveTimer.Tick += (_, _) =>
            {
                _saveTimer.Stop();
                SaveWindowState();
            };
            Move += (_, _) => QueueWindowStateSave();
            Resize += (_, _) => QueueWindowStateSave();
        }

        public Size RenderSize => ClientSize;

        public void EnableVisual(
            string title,
            ComponentRenderer renderer,
            LiveSplitState state,
            LiveSplit.Options.LayoutSettings layoutSettings,
            string dataDirectory,
            IReadOnlyList<HostedComponentContext> components,
            Action<string> openSettings,
            Action<string> disablePlugin)
        {
            _renderer = renderer;
            _state = state;
            _layoutSettings = layoutSettings;
            _components = components;
            _openSettings = openSettings;
            _disablePlugin = disablePlugin;
            _windowStatePath = Path.Combine(dataDirectory, "window.json");
            Text = title;
            Controls.Clear();
            Opacity = 1;
            ShowInTaskbar = true;
            FormBorderStyle = Forms.FormBorderStyle.None;
            Padding = Forms.Padding.Empty;
            BackColor = Color.Black;
            StartPosition = Forms.FormStartPosition.Manual;

            int width = Math.Max(300, (int)Math.Ceiling(renderer.MinimumWidth));
            int height = Math.Max(25, (int)Math.Ceiling(Math.Max(renderer.OverallSize, renderer.MinimumHeight)));
            _lastNaturalHeight = height;
            MinimumSize = new Size(Math.Max(100, (int)Math.Ceiling(renderer.MinimumWidth)), 25);
            RestoreWindowState(width, height);
            _visualEnabled = true;
            UpdateLayoutSize();
            BringToFront();
            Invalidate();
        }

        public void SetOverallSize(float overallSize, float minimumHeight)
        {
            int naturalHeight = Math.Max(25, (int)Math.Ceiling(Math.Max(overallSize, minimumHeight)));
            if (naturalHeight == _lastNaturalHeight) return;
            int previous = Math.Max(1, _lastNaturalHeight);
            int requested = Math.Max(25, (int)Math.Round(ClientSize.Height * (naturalHeight / (double)previous)));
            _lastNaturalHeight = naturalHeight;
            ClientSize = new Size(ClientSize.Width, requested);
        }

        public void InvalidateRenderSurface() => Invalidate();

        public void SaveWindowState()
        {
            if (!_visualEnabled || _restoringWindow || _windowStatePath is null || WindowState != Forms.FormWindowState.Normal) return;
            try
            {
                var state = new HostWindowState
                {
                    X = Left,
                    Y = Top,
                    Width = ClientSize.Width,
                    Height = ClientSize.Height,
                    NaturalHeight = _lastNaturalHeight
                };
                string temporary = _windowStatePath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(_windowStatePath)!);
                File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, _windowStatePath, true);
            }
            catch { }
        }

        protected override void OnPaint(Forms.PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            if (_renderer is null || _state is null || _layoutSettings is null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            Graphics graphics = eventArgs.Graphics;
            using Region updateRegion = graphics.Clip;
            graphics.Clip = new Region();
            graphics.TextRenderingHint = _layoutSettings.AntiAliasing ? TextRenderingHint.AntiAlias : TextRenderingHint.ClearTypeGridFit;
            graphics.CompositingQuality = CompositingQuality.GammaCorrected;
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(_layoutSettings.BackgroundColor == Color.Transparent ? Color.Black : _layoutSettings.BackgroundColor);
            _renderer.CalculateOverallSize(LayoutMode.Vertical);
            float scale = ClientSize.Height / _renderer.OverallSize;
            if (!float.IsFinite(scale) || scale <= 0) return;
            graphics.ResetTransform();
            graphics.TranslateTransform(-0.5F, -0.5F);
            graphics.ScaleTransform(scale, scale);
            float transformedWidth = ClientSize.Width / scale;
            _renderer.Render(graphics, _state, transformedWidth, ClientSize.Height, LayoutMode.Vertical, updateRegion);
        }

        protected override void OnResize(EventArgs eventArgs)
        {
            base.OnResize(eventArgs);
            UpdateLayoutSize();
        }

        private void UpdateLayoutSize()
        {
            if (_state?.Layout is null) return;
            _state.Layout.VerticalWidth = ClientSize.Width;
            _state.Layout.VerticalHeight = ClientSize.Height;
        }

        private void DragWindow(object? sender, Forms.MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != Forms.MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, 0x00A1, 2, IntPtr.Zero);
        }

        protected override void OnMouseUp(Forms.MouseEventArgs eventArgs)
        {
            base.OnMouseUp(eventArgs);
            if (eventArgs.Button != Forms.MouseButtons.Right) return;
            HostedComponentContext? selected = FindComponentAt(eventArgs.Y);
            if (selected is null) return;

            var menu = new Forms.ContextMenuStrip();
            var settings = new Forms.ToolStripMenuItem("Open Plugin Settings");
            settings.Click += (_, _) => _openSettings?.Invoke(selected.Id);
            var disable = new Forms.ToolStripMenuItem("Disable Plugin");
            disable.Click += (_, _) => _disablePlugin?.Invoke(selected.Id);
            menu.Items.Add(settings);
            menu.Items.Add(disable);
            menu.Show(this, eventArgs.Location);
        }

        protected override void WndProc(ref Forms.Message message)
        {
            const int wmNcHitTest = 0x0084;
            const int htClient = 1;
            const int edge = 7;
            base.WndProc(ref message);
            if (!_visualEnabled || message.Msg != wmNcHitTest || message.Result != (IntPtr)htClient) return;

            long packed = message.LParam.ToInt64();
            Point point = PointToClient(new Point(unchecked((short)(packed & 0xffff)), unchecked((short)((packed >> 16) & 0xffff))));
            bool left = point.X < edge;
            bool right = point.X >= ClientSize.Width - edge;
            bool top = point.Y < edge;
            bool bottom = point.Y >= ClientSize.Height - edge;
            message.Result = (IntPtr)((left, right, top, bottom) switch
            {
                (true, _, true, _) => 13,
                (_, true, true, _) => 14,
                (true, _, _, true) => 16,
                (_, true, _, true) => 17,
                (true, _, _, _) => 10,
                (_, true, _, _) => 11,
                (_, _, true, _) => 12,
                (_, _, _, true) => 15,
                _ => htClient
            });
        }

        private HostedComponentContext? FindComponentAt(int clientY)
        {
            if (_renderer is null || _renderer.OverallSize <= 0 || ClientSize.Height <= 0) return null;
            float logicalY = clientY / (ClientSize.Height / _renderer.OverallSize);
            float offset = 0;
            foreach (HostedComponentContext entry in _components)
            {
                float height = Math.Max(0, entry.Component.VerticalHeight);
                if (height > 0 && logicalY >= offset && logicalY < offset + height) return entry;
                offset += height;
            }
            return null;
        }

        private void RestoreWindowState(int defaultWidth, int defaultHeight)
        {
            _restoringWindow = true;
            try
            {
                HostWindowState? saved = null;
                try
                {
                    if (_windowStatePath is not null && File.Exists(_windowStatePath))
                        saved = JsonSerializer.Deserialize<HostWindowState>(File.ReadAllText(_windowStatePath));
                }
                catch { }

                int width = saved?.Width > 0 ? saved.Width : defaultWidth;
                int height = saved?.Height > 0 ? saved.Height : defaultHeight;
                if (saved is { NaturalHeight: > 0 } && saved.NaturalHeight != defaultHeight)
                    height = Math.Max(25, (int)Math.Round(height * (defaultHeight / (double)saved.NaturalHeight)));
                ClientSize = new Size(Math.Max(MinimumSize.Width, width), Math.Max(MinimumSize.Height, height));

                Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
                Rectangle candidate = saved is null
                    ? new Rectangle(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2, Width, Height)
                    : new Rectangle(saved.X, saved.Y, Width, Height);
                bool visible = Screen.AllScreens.Any(screen => Rectangle.Intersect(screen.WorkingArea, candidate) is { Width: >= 32, Height: >= 32 });
                Location = visible ? candidate.Location : new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }
            finally
            {
                _restoringWindow = false;
            }
        }

        private void QueueWindowStateSave()
        {
            if (!_visualEnabled || _restoringWindow) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SaveWindowState();
                _saveTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class HostWindowState
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int NaturalHeight { get; set; }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
            public static extern bool ReleaseCapture();
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr window, int message, int wParam, IntPtr lParam);
        }
    }

    private sealed class HostOptions
    {
        public required string Title { get; init; }
        public required IReadOnlyList<PluginHostedComponent> Components { get; init; }
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
            string manifestPath = Path.GetFullPath(Required(values, "--manifest"));
            PluginHostManifest manifest = PluginHostManifest.Load(manifestPath);
            if (manifest.Components.Count == 0) throw new InvalidDataException("The Plugin host manifest contains no components.");
            foreach (PluginHostedComponent component in manifest.Components)
            {
                component.AssemblyPath = Path.GetFullPath(component.AssemblyPath);
                component.SettingsPath = Path.GetFullPath(component.SettingsPath);
                if (!File.Exists(component.AssemblyPath)) throw new FileNotFoundException("A plugin assembly is missing.", component.AssemblyPath);
            }
            return new HostOptions
            {
                Title = string.IsNullOrWhiteSpace(manifest.Title) ? "Launcher Plugins" : manifest.Title,
                Components = manifest.Components.OrderBy(component => component.LayoutOrder).ToArray(),
                DataDirectory = Path.GetFullPath(Required(values, "--data")),
                LogsDirectory = Path.GetFullPath(Required(values, "--logs")),
                StopPath = Path.GetFullPath(Required(values, "--stop")),
                ParentProcessId = parent
            };
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Missing required argument {key}.");
    }
}
