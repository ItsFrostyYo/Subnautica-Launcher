using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Model.Comparisons;
using LiveSplit.Options;
using LiveSplit.Options.SettingsFactories;
using LiveSplit.UI;
using LiveSplit.UI.Components;
using Forms = System.Windows.Forms;

namespace SubnauticaLauncher.Services;

/// <summary>
/// Hosts the component-provided WinForms settings control using LiveSplit's
/// GetSettingsControl/GetSettings/SetSettings lifecycle. The surrounding frame
/// belongs to the launcher; the settings UI itself belongs entirely to the plugin.
/// </summary>
internal static class PluginSettingsHostMode
{
    public const string Command = "--plugin-settings-host";

    public static bool IsRequested(string[] args) =>
        args.Length > 0 && string.Equals(args[0], Command, StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        SettingsHostOptions? options = null;
        IComponent? component = null;
        PluginSettingsForm? form = null;
        try
        {
            options = SettingsHostOptions.Parse(args.Skip(1).ToArray());
            Directory.CreateDirectory(options.DataDirectory);
            Directory.CreateDirectory(options.LogsDirectory);
            AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", Path.TrimEndingDirectorySeparator(options.LogsDirectory) + Path.DirectorySeparatorChar);
            AssemblyLoadContext.Default.Resolving += ResolveAssembly;
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);

            form = new PluginSettingsForm(options.Title, options.OwnerHandle, options.ParentProcessId);
            LiveSplitState state = CreateState(form);
            component = CreateComponent(options, state);
            LoadSettings(component, options.SettingsPath);

            // This ordering deliberately matches LiveSplit's ComponentSettingsDialog:
            // obtain the real control, attach it, then snapshot the component XML.
            Forms.Control settingsControl = component.GetSettingsControl(LayoutMode.Vertical)
                ?? throw new InvalidOperationException($"{component.ComponentName} did not provide a settings control.");
            var snapshotDocument = new XmlDocument();
            XmlNode snapshot = component.GetSettings(snapshotDocument);
            form.AttachSettingsControl(settingsControl);

            Forms.DialogResult result = form.ShowDialog();
            if (result == Forms.DialogResult.OK)
            {
                SaveSettings(component, options.SettingsPath);
                Console.Out.WriteLine("SAVED");
            }
            else
            {
                // LiveSplit restores the original XML when Cancel is pressed.
                component.SetSettings(snapshot);
                Console.Out.WriteLine("CANCELLED");
            }
            Console.Out.Flush();
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(Unwrap(exception));
            return 1;
        }
        finally
        {
            try { component?.Dispose(); } catch (Exception exception) { Console.Error.WriteLine(Unwrap(exception)); }
            try { form?.Dispose(); } catch { }
            if (options is not null) AssemblyLoadContext.Default.Resolving -= ResolveAssembly;
        }

        Assembly? ResolveAssembly(AssemblyLoadContext context, AssemblyName assemblyName)
        {
            if (options is null) return null;
            string candidate = Path.Combine(Path.GetDirectoryName(options.AssemblyPath)!, assemblyName.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        }
    }

    internal static Forms.Form ShowForHostedComponent(
        IComponent component,
        string settingsPath,
        string title,
        Forms.Form owner,
        int parentProcessId,
        Action<bool>? completed = null)
    {
        Forms.Control settingsControl = component.GetSettingsControl(LayoutMode.Vertical)
            ?? throw new InvalidOperationException($"{component.ComponentName} did not provide a settings control.");
        var snapshotDocument = new XmlDocument();
        XmlNode snapshot = component.GetSettings(snapshotDocument);
        var form = new PluginSettingsForm(title, owner.Handle, parentProcessId);
        form.AttachSettingsControl(settingsControl);
        form.FormClosed += (_, _) =>
        {
            bool saved = form.DialogResult == Forms.DialogResult.OK;
            try
            {
                if (saved) SaveSettings(component, settingsPath);
                else component.SetSettings(snapshot);
            }
            finally
            {
                completed?.Invoke(saved);
            }
        };
        form.Show(owner);
        return form;
    }

    private static LiveSplitState CreateState(Forms.Form form)
    {
        LiveSplit.Options.LayoutSettings layoutSettings = new StandardLayoutSettingsFactory().Create();
        var layout = new Layout { Settings = layoutSettings, Mode = LayoutMode.Vertical };
        var run = new Run(new StandardComparisonGeneratorsFactory());
        run.Add(new Segment("Launcher", new Time(), new Time(), null, new Time()));
        var state = new LiveSplitState(run, form, layout, layoutSettings, new Settings());
        state.RegisterTimerModel(new TimerModel { CurrentState = state });
        return state;
    }

    private static IComponent CreateComponent(SettingsHostOptions options, LiveSplitState state)
    {
        Assembly assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(options.AssemblyPath);
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException exception) { types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray(); }

        List<IComponentFactory> factories = types
            .Where(type => !type.IsAbstract && typeof(IComponentFactory).IsAssignableFrom(type))
            .Select(type => Activator.CreateInstance(type, nonPublic: true))
            .OfType<IComponentFactory>()
            .ToList();
        IComponentFactory? selected = factories.FirstOrDefault(factory =>
            string.Equals(factory.ComponentName, options.ComponentName, StringComparison.OrdinalIgnoreCase));
        if (selected is null && factories.Count == 1) selected = factories[0];
        if (selected is null)
            throw new InvalidDataException($"The Plugin component '{options.ComponentName}' was not found in {Path.GetFileName(options.AssemblyPath)}.");
        return selected.Create(state) ?? throw new InvalidOperationException($"{selected.ComponentName} returned no component.");
    }

    private static void LoadSettings(IComponent component, string path)
    {
        if (!File.Exists(path)) return;
        var document = new XmlDocument();
        document.Load(path);
        if (document.DocumentElement is null) throw new InvalidDataException("The plugin settings XML has no root element.");
        component.SetSettings(document.DocumentElement);
    }

    private static void SaveSettings(IComponent component, string path)
    {
        var document = new XmlDocument();
        document.AppendChild(component.GetSettings(document));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".settings.tmp";
        document.Save(temporary);
        File.Move(temporary, path, true);
    }

    private static Exception Unwrap(Exception exception)
    {
        while (exception is TargetInvocationException { InnerException: not null }) exception = exception.InnerException;
        return exception;
    }

    private sealed class PluginSettingsForm : Forms.Form
    {
        private const int TitleHeight = 30;
        private const int FooterHeight = 54;
        private readonly Forms.Panel _content;
        private readonly Forms.Timer _parentTimer;
        private readonly int _parentProcessId;
        private readonly IntPtr _ownerHandle;
        private Forms.Control? _settingsControl;

        public PluginSettingsForm(string title, IntPtr ownerHandle, int parentProcessId)
        {
            _parentProcessId = parentProcessId;
            _ownerHandle = ownerHandle;
            Text = title;
            FormBorderStyle = Forms.FormBorderStyle.None;
            Padding = new Forms.Padding(1);
            StartPosition = Forms.FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(54, 69, 89);
            MinimumSize = new Size(420, 260);
            KeyPreview = true;

            var titleBar = new Forms.Panel { Dock = Forms.DockStyle.Top, Height = TitleHeight, BackColor = Color.FromArgb(16, 23, 37) };
            var titleLabel = new Forms.Label
            {
                Text = title,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                Dock = Forms.DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Forms.Padding(12, 0, 0, 0),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
            var close = new LauncherCloseButton { Anchor = Forms.AnchorStyles.Top | Forms.AnchorStyles.Right };
            titleBar.Resize += (_, _) => close.Location = new Point(titleBar.ClientSize.Width - 17 - close.Width, 8);
            close.Click += (_, _) => CancelAndClose();
            titleBar.Controls.Add(titleLabel);
            titleBar.Controls.Add(close);
            close.BringToFront();
            titleBar.MouseDown += DragWindow;
            titleLabel.MouseDown += DragWindow;

            var footer = new Forms.Panel { Dock = Forms.DockStyle.Bottom, Height = FooterHeight, BackColor = SystemColors.Control };
            var save = new Forms.Button { Text = "OK", DialogResult = Forms.DialogResult.OK, Size = new Size(88, 30), Anchor = Forms.AnchorStyles.Right | Forms.AnchorStyles.Bottom };
            var cancel = new Forms.Button { Text = "Cancel", DialogResult = Forms.DialogResult.Cancel, Size = new Size(88, 30), Anchor = Forms.AnchorStyles.Right | Forms.AnchorStyles.Bottom };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                cancel.Location = new Point(footer.ClientSize.Width - cancel.Width - 12, 12);
                save.Location = new Point(cancel.Left - save.Width - 8, 12);
            };
            save.Click += (_, _) => { DialogResult = Forms.DialogResult.OK; Close(); };
            cancel.Click += (_, _) => CancelAndClose();
            AcceptButton = save;
            CancelButton = cancel;

            _content = new Forms.Panel { Dock = Forms.DockStyle.Fill, AutoScroll = true, BackColor = SystemColors.Control, Padding = new Forms.Padding(12) };
            Controls.Add(_content);
            Controls.Add(footer);
            Controls.Add(titleBar);

            if (ownerHandle != IntPtr.Zero)
            {
                NativeMethods.SetWindowLongPtr(Handle, NativeMethods.GwlpHwndParent, ownerHandle);
                CenterOverOwner(ownerHandle);
            }
            else
            {
                Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
                Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            }

            _parentTimer = new Forms.Timer { Interval = 500, Enabled = true };
            _parentTimer.Tick += (_, _) =>
            {
                try { using Process parent = Process.GetProcessById(_parentProcessId); if (!parent.HasExited) return; }
                catch { }
                DialogResult = Forms.DialogResult.Cancel;
                Close();
            };
            FormClosing += (_, eventArgs) =>
            {
                if (DialogResult == Forms.DialogResult.OK) return;
                DialogResult = Forms.DialogResult.Cancel;
            };
            Shown += (_, _) => NativeMethods.TryEnableRoundedCorners(Handle);
        }

        public void AttachSettingsControl(Forms.Control control)
        {
            _settingsControl = control;
            Size preferred = control.PreferredSize;
            int contentWidth = Math.Max(control.Width, preferred.Width);
            int contentHeight = Math.Max(control.Height, preferred.Height);
            ClientSize = new Size(Math.Clamp(contentWidth + 24, 420, 900), Math.Clamp(contentHeight + TitleHeight + FooterHeight + 24, 260, 800));
            control.Location = new Point(0, 0);
            control.Anchor = Forms.AnchorStyles.Top | Forms.AnchorStyles.Left;
            _content.AutoScrollMinSize = new Size(contentWidth, contentHeight);
            _content.Controls.Add(control);
            if (_ownerHandle != IntPtr.Zero) CenterOverOwner(_ownerHandle);
        }

        private void CancelAndClose()
        {
            DialogResult = Forms.DialogResult.Cancel;
            Close();
        }

        private void CenterOverOwner(IntPtr owner)
        {
            if (!NativeMethods.GetWindowRect(owner, out NativeMethods.Rect rect)) return;
            Location = new Point(rect.Left + (rect.Width - Width) / 2, rect.Top + (rect.Height - Height) / 2);
        }

        private void DragWindow(object? sender, Forms.MouseEventArgs eventArgs)
        {
            if (eventArgs.Button != Forms.MouseButtons.Left) return;
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessage(Handle, NativeMethods.WmNcLButtonDown, NativeMethods.HtCaption, IntPtr.Zero);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _parentTimer.Dispose();
                if (_settingsControl is not null && _content.Controls.Contains(_settingsControl))
                    _content.Controls.Remove(_settingsControl);
            }
            base.Dispose(disposing);
        }
    }

    private sealed class SettingsHostOptions
    {
        public required string AssemblyPath { get; init; }
        public required string ComponentName { get; init; }
        public required string SettingsPath { get; init; }
        public required string DataDirectory { get; init; }
        public required string LogsDirectory { get; init; }
        public required string Title { get; init; }
        public required IntPtr OwnerHandle { get; init; }
        public required int ParentProcessId { get; init; }

        public static SettingsHostOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index + 1 < args.Length; index += 2) values[args[index]] = args[index + 1];
            if (!int.TryParse(Required(values, "--parent"), out int parent) || parent <= 0)
                throw new ArgumentException("The settings host requires a valid launcher process ID.");
            if (!long.TryParse(Required(values, "--owner"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long owner))
                throw new ArgumentException("The settings host requires a valid owner window handle.");
            string assemblyPath = Path.GetFullPath(Required(values, "--assembly"));
            if (!File.Exists(assemblyPath)) throw new FileNotFoundException("The plugin assembly is missing.", assemblyPath);
            return new SettingsHostOptions
            {
                AssemblyPath = assemblyPath,
                ComponentName = Required(values, "--component"),
                SettingsPath = Path.GetFullPath(Required(values, "--settings")),
                DataDirectory = Path.GetFullPath(Required(values, "--data")),
                LogsDirectory = Path.GetFullPath(Required(values, "--logs")),
                Title = Required(values, "--title"),
                OwnerHandle = new IntPtr(owner),
                ParentProcessId = parent
            };
        }

        private static string Required(IReadOnlyDictionary<string, string> values, string key) =>
            values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ArgumentException($"Missing required argument {key}.");
    }

    private static class NativeMethods
    {
        public const int GwlpHwndParent = -8;
        public const int WmNcLButtonDown = 0x00A1;
        public const int HtCaption = 2;
        private const int DwmWindowCornerPreference = 33;
        private const int DwmWindowCornerRound = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rectangle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr window, int message, int wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        public static void TryEnableRoundedCorners(IntPtr window)
        {
            try { int value = DwmWindowCornerRound; _ = DwmSetWindowAttribute(window, DwmWindowCornerPreference, ref value, sizeof(int)); }
            catch { }
        }
    }
}
