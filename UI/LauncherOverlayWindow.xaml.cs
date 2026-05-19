using SubnauticaLauncher.Enums;
using SubnauticaLauncher.Explosion;
using SubnauticaLauncher.Gameplay;
using SubnauticaLauncher.Settings;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Versions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Color = System.Windows.Media.Color;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using Separator = System.Windows.Controls.Separator;
using TextBox = System.Windows.Controls.TextBox;
using UpdatesData = SubnauticaLauncher.Updates.Updates;

namespace SubnauticaLauncher.UI
{
    public partial class LauncherOverlayWindow : Window
    {
        private const double TopMargin = 78;
        private const double PanelGap = 12;
        private const int GWL_EXSTYLE = -20;
        private const int GW_OWNER = 4;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private readonly MainWindow _main;
        private readonly DispatcherTimer _placementTimer;
        private readonly Dictionary<GameOverlayComponentType, GameOverlayComponentLayout> _layouts = new();
        private readonly Dictionary<GameOverlayComponentType, Border> _componentRoots = new();
        private readonly Dictionary<GameOverlayComponentType, VersionListPanelState> _versionPanels = new();
        private readonly Dictionary<LauncherGame, string?> _selectedFoldersByGame = new();
        private LauncherSettingsPanelState? _launcherSettingsPanel;
        private ResetMacrosPanelState? _resetMacrosPanel;
        private OtherToolsPanelState? _otherToolsPanel;
        private LauncherInfoPanelState? _launcherInfoPanel;
        private bool _allowClose;
        private bool _capturingOverlayHotkey;
        private bool _capturingResetHotkey;
        private bool _syncingLauncherSettings;
        private bool _syncingResetGamemode;
        private bool _syncingExplosionPreset;
        private bool _syncingExplosionCustomRange;
        private bool _syncingVersionSelections;
        private GameOverlayComponentType? _draggingType;
        private Point _dragStartMouse;
        private double _dragStartLeft;
        private double _dragStartTop;
        private double _panelOpacity = 0.5;
        private LauncherGame? _currentTargetGame;
        private LauncherGame? _preferredSelectedGame;

        private sealed class VersionListPanelState
        {
            public required GameOverlayComponentType Type { get; init; }
            public required LauncherGame Game { get; init; }
            public required Border Root { get; init; }
            public required ListBox ListBox { get; init; }
            public required Button LaunchButton { get; init; }
            public required Button SwitchButton { get; init; }
            public bool ShowLabels { get; set; } = true;
        }

        private sealed class LauncherSettingsPanelState
        {
            public required Border Root { get; init; }
            public required Button ExplosionOverlayButton { get; init; }
            public required Button ExplosionTrackingButton { get; init; }
            public required ComboBox BackgroundDropdown { get; init; }
            public required Button ChooseCustomBackgroundButton { get; init; }
        }

        private sealed class ResetMacrosPanelState
        {
            public required Border Root { get; init; }
            public required Button ResetMacroButton { get; init; }
            public required TextBox ResetHotkeyBox { get; init; }
            public required ComboBox ResetGamemodeDropdown { get; init; }
            public required StackPanel SubnauticaOnlySection { get; init; }
            public required Button ExplosionResetButton { get; init; }
            public required ComboBox ExplosionPresetDropdown { get; init; }
            public required Grid ExplosionCustomRangePanel { get; init; }
            public required TextBox ExplosionCustomMinBox { get; init; }
            public required TextBox ExplosionCustomMaxBox { get; init; }
        }

        private sealed class OtherToolsPanelState
        {
            public required Border Root { get; init; }
            public required Button HardcoreButton { get; init; }
            public required StackPanel TrackerButtonsPanel { get; init; }
        }

        private sealed class LauncherInfoPanelState
        {
            public required Border Root { get; init; }
            public required StackPanel UpdatesPanel { get; init; }
        }

        public LauncherOverlayWindow(MainWindow main)
        {
            _main = main;
            InitializeComponent();

            AddHandler(Keyboard.PreviewKeyDownEvent, new System.Windows.Input.KeyEventHandler(LauncherOverlayWindow_PreviewKeyDown), true);
            IsVisibleChanged += LauncherOverlayWindow_IsVisibleChanged;

            _placementTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };
            _placementTimer.Tick += PlacementTimer_Tick;
        }

        public void AllowClose()
        {
            _allowClose = true;
        }

        public void ApplyOverlayOpacity(double value)
        {
            _panelOpacity = Math.Clamp(value, 0.2, 1);
            TopBarBorder.Background = CreatePanelBrush(0.92);

            foreach ((GameOverlayComponentType type, Border root) in _componentRoots)
            {
                double effectiveOpacity = _layouts.TryGetValue(type, out GameOverlayComponentLayout? layout)
                    ? GetPanelOpacity(layout)
                    : _panelOpacity;
                root.Background = CreatePanelBrush(effectiveOpacity);
            }
        }

        public void RefreshFromMain()
        {
            _main.RefreshRunningStateForOverlay();
            LoadLayoutsFromSettings();
            SyncPanelsToLayouts();
            RefreshVersionStatusOnly();
            UpdateTopBarText();
            UpdatePlacement();
        }

        public void RefreshVersionStatusOnly()
        {
            foreach (VersionListPanelState panel in _versionPanels.Values)
                RefreshVersionListPanel(panel);

            RefreshLauncherSettingsPanel();
            RefreshResetMacrosPanel();
            RefreshOtherToolsPanel();
            RefreshLauncherInfoPanel();
            UpdateTopBarText();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_allowClose)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();
            base.OnClosing(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            UpdatePlacement();
        }

        private void LauncherOverlayWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                _placementTimer.Start();
                UpdatePlacement();
                Activate();
                Focus();
            }
            else
            {
                _placementTimer.Stop();
            }
        }

        private void PlacementTimer_Tick(object? sender, EventArgs e)
        {
            UpdatePlacement();
        }

        private void UpdatePlacement()
        {
            if (!TryGetOverlayTargetRect(out Rect rect, out LauncherGame? game))
            {
                Rect workArea = SystemParameters.WorkArea;
                rect = new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);
            }

            if (_currentTargetGame != game)
            {
                _currentTargetGame = game;
                RefreshResetMacrosPanel();
                RefreshOtherToolsPanel();
                UpdateTopBarText();
            }

            if (Math.Abs(Left - rect.Left) > 0.5 ||
                Math.Abs(Top - rect.Top) > 0.5 ||
                Math.Abs(Width - rect.Width) > 0.5 ||
                Math.Abs(Height - rect.Height) > 0.5)
            {
                Left = rect.Left;
                Top = rect.Top;
                Width = rect.Width;
                Height = rect.Height;
                OverlayCanvas.Width = rect.Width;
                OverlayCanvas.Height = rect.Height;
                ClampAllPanelsToBounds(persist: false);
            }

        }

        private bool TryGetOverlayTargetRect(out Rect rect, out LauncherGame? game)
        {
            rect = default;
            game = null;

            GameProcessSnapshot snapshot = GameProcessMonitor.GetSnapshot();

            IntPtr foreground = GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                _ = GetWindowThreadProcessId(foreground, out uint foregroundPid);
                if (foregroundPid != 0)
                {
                    foreach ((LauncherGame candidateGame, int? processId) in EnumerateRunningGames(snapshot))
                    {
                        if (processId == foregroundPid &&
                            TryGetBestWindowRectForProcess(processId.Value, out Rect foregroundRect))
                        {
                            rect = foregroundRect;
                            game = candidateGame;
                            return true;
                        }
                    }
                }
            }

            if (_currentTargetGame.HasValue &&
                TryGetRectForGame(snapshot, _currentTargetGame.Value, out rect))
            {
                game = _currentTargetGame.Value;
                return true;
            }

            foreach ((LauncherGame candidateGame, _) in EnumerateRunningGames(snapshot))
            {
                if (TryGetRectForGame(snapshot, candidateGame, out rect))
                {
                    game = candidateGame;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<(LauncherGame Game, int? ProcessId)> EnumerateRunningGames(GameProcessSnapshot snapshot)
        {
            yield return (LauncherGame.Subnautica, snapshot.Subnautica.ProcessId);
            yield return (LauncherGame.BelowZero, snapshot.BelowZero.ProcessId);
            yield return (LauncherGame.Subnautica2, snapshot.Subnautica2.ProcessId);
        }

        private static bool TryGetRectForGame(GameProcessSnapshot snapshot, LauncherGame game, out Rect rect)
        {
            int? processId = game switch
            {
                LauncherGame.BelowZero => snapshot.BelowZero.ProcessId,
                LauncherGame.Subnautica2 => snapshot.Subnautica2.ProcessId,
                _ => snapshot.Subnautica.ProcessId
            };

            rect = default;
            if (processId is not int pid)
                return false;

            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return false;

                if (!TryGetBestWindowRectForProcess(pid, out Rect bestRect))
                    return false;

                rect = bestRect;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Rect ToRect(RECT rect)
        {
            return new Rect(rect.Left, rect.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
        }

        private sealed class BestWindowSearchState
        {
            public BestWindowSearchState(uint processId)
            {
                ProcessId = processId;
            }

            public uint ProcessId { get; }
            public double BestArea { get; set; }
            public RECT BestRect { get; set; }
            public bool Found { get; set; }
        }

        private static bool TryGetBestWindowRectForProcess(int processId, out Rect rect)
        {
            rect = default;
            BestWindowSearchState state = new((uint)processId);
            GCHandle handle = GCHandle.Alloc(state);
            try
            {
                EnumWindows(static (hWnd, lParam) =>
                {
                    GCHandle localHandle = GCHandle.FromIntPtr(lParam);
                    var localState = (BestWindowSearchState)localHandle.Target!;

                    _ = GetWindowThreadProcessId(hWnd, out uint windowPid);
                    if (windowPid != localState.ProcessId)
                        return true;

                    if (!IsWindowVisible(hWnd) || IsIconic(hWnd))
                        return true;

                    if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                        return true;

                    int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                    if ((exStyle & WS_EX_TOOLWINDOW) != 0)
                        return true;

                    string className = GetWindowClassName(hWnd);
                    if (string.Equals(className, "ConsoleWindowClass", StringComparison.OrdinalIgnoreCase))
                        return true;

                    if (!GetWindowRect(hWnd, out RECT nativeRect))
                        return true;

                    double width = Math.Max(0, nativeRect.Right - nativeRect.Left);
                    double height = Math.Max(0, nativeRect.Bottom - nativeRect.Top);
                    double area = width * height;
                    if (area < 40000)
                        return true;

                    if (area > localState.BestArea)
                    {
                        localState.BestArea = area;
                        localState.BestRect = nativeRect;
                        localState.Found = true;
                    }

                    return true;
                }, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                if (handle.IsAllocated)
                    handle.Free();
            }

            if (!state.Found)
                return false;

            rect = ToRect(state.BestRect);
            return true;
        }

        private void LoadLayoutsFromSettings()
        {
            _layouts.Clear();

            foreach (GameOverlayComponentLayout layout in LauncherSettings.Current.GameOverlayComponents ?? new())
                _layouts[layout.Type] = layout;

            if (!LauncherSettings.Current.GameOverlayLayoutMigrated &&
                ShouldClearLegacyDefaultLayout(_layouts.Values.ToList()))
            {
                _layouts.Clear();
                LauncherSettings.Current.GameOverlayLayoutMigrated = true;
                SaveLayouts();
            }
            else if (!LauncherSettings.Current.GameOverlayLayoutMigrated)
            {
                LauncherSettings.Current.GameOverlayLayoutMigrated = true;
                LauncherSettings.Save();
            }
        }

        private void SaveLayouts()
        {
            LauncherSettings.Current.GameOverlayComponents = _layouts.Values
                .OrderBy(v => v.Type.ToString(), StringComparer.Ordinal)
                .ToList();
            LauncherSettings.Save();
        }

        private void SyncPanelsToLayouts()
        {
            foreach (GameOverlayComponentType removedType in _componentRoots.Keys.Except(_layouts.Keys).ToList())
                RemoveComponentVisual(removedType);

            foreach ((GameOverlayComponentType type, GameOverlayComponentLayout layout) in _layouts)
            {
                if (_componentRoots.ContainsKey(type))
                {
                    if (_versionPanels.TryGetValue(type, out VersionListPanelState? panel))
                        panel.ShowLabels = layout.ShowLabels;

                    ApplyPanelPosition(type, layout.Left, layout.Top);
                    continue;
                }

                AddComponentVisual(type, layout);
            }

            ClampAllPanelsToBounds(persist: false);
        }

        private void AddComponentVisual(GameOverlayComponentType type, GameOverlayComponentLayout layout)
        {
            Border root = type switch
            {
                GameOverlayComponentType.SubnauticaVersionList => CreateVersionListPanel(type, LauncherGame.Subnautica, "Subnautica Version List"),
                GameOverlayComponentType.BelowZeroVersionList => CreateVersionListPanel(type, LauncherGame.BelowZero, "Below Zero Version List"),
                GameOverlayComponentType.Subnautica2VersionList => CreateVersionListPanel(type, LauncherGame.Subnautica2, "Subnautica 2 Version List"),
                GameOverlayComponentType.LauncherSettings => CreateLauncherSettingsPanel(type),
                GameOverlayComponentType.ResetMacros => CreateResetMacrosPanel(type),
                GameOverlayComponentType.OtherTools => CreateOtherToolsPanel(type),
                _ => CreateLauncherInfoPanel(type)
            };

            root.Background = CreatePanelBrush(GetPanelOpacity(layout));
            OverlayCanvas.Children.Add(root);
            _componentRoots[type] = root;
            ApplyPanelPosition(type, layout.Left, layout.Top);
        }

        private void RemoveComponentVisual(GameOverlayComponentType type)
        {
            if (_componentRoots.TryGetValue(type, out Border? root))
                OverlayCanvas.Children.Remove(root);

            _componentRoots.Remove(type);
            _versionPanels.Remove(type);

            if (type == GameOverlayComponentType.LauncherSettings)
                _launcherSettingsPanel = null;
            else if (type == GameOverlayComponentType.ResetMacros)
                _resetMacrosPanel = null;
            else if (type == GameOverlayComponentType.OtherTools)
                _otherToolsPanel = null;
            else if (type == GameOverlayComponentType.LauncherInfo)
                _launcherInfoPanel = null;
        }

        private Border CreateVersionListPanel(GameOverlayComponentType type, LauncherGame game, string title)
        {
            ListBox listBox = new()
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                ItemContainerStyle = (Style)Resources["OverlayVersionListItemStyle"],
                ItemTemplate = (DataTemplate)Resources["OverlayVersionRowTemplate"]
            };
            ScrollViewer.SetVerticalScrollBarVisibility(listBox, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
            listBox.SelectionChanged += OverlayVersionList_SelectionChanged;
            listBox.Tag = game;

            Button launchButton = new()
            {
                Content = "Launch",
                Width = 112,
                Background = (Brush)FindResource("OverlayLaunchAccentBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Tag = game
            };
            launchButton.Click += OverlayLaunchButton_Click;

            Button switchButton = new()
            {
                Content = "Switch",
                Width = 112,
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Tag = game
            };
            switchButton.Click += OverlaySwitchButton_Click;

            Grid content = new();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Border listCard = new()
            {
                Background = (Brush)FindResource("OverlayVersionListCardBrush"),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(6),
                Child = listBox
            };

            StackPanel buttons = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            buttons.Children.Add(launchButton);
            buttons.Children.Add(new Border { Width = 10, Background = Brushes.Transparent });
            buttons.Children.Add(switchButton);

            content.Children.Add(listCard);
            Grid.SetRow(buttons, 1);
            content.Children.Add(buttons);

            Border root = CreatePanelShell(type, title, 360, 470, showSettingsButton: true, content, scrollContent: false);

            _versionPanels[type] = new VersionListPanelState
            {
                Type = type,
                Game = game,
                Root = root,
                ListBox = listBox,
                LaunchButton = launchButton,
                SwitchButton = switchButton,
                ShowLabels = _layouts.TryGetValue(type, out GameOverlayComponentLayout? layout) ? layout.ShowLabels : true
            };

            return root;
        }

        private Border CreateLauncherSettingsPanel(GameOverlayComponentType type)
        {
            Button explosionOverlayButton = CreateToggleButton();
            explosionOverlayButton.Click += (_, _) => _main.ToggleExplosionOverlayFromOverlay();

            Button explosionTrackingButton = CreateToggleButton();
            explosionTrackingButton.Click += (_, _) => _main.ToggleExplosionTrackingFromOverlay();

            ComboBox backgroundDropdown = new()
            {
                Style = (Style)Resources["OverlayComboBoxStyle"]
            };
            foreach (string option in _main.GetBackgroundPresetOptionsForOverlay())
                backgroundDropdown.Items.Add(new ComboBoxItem { Content = option });
            backgroundDropdown.SelectionChanged += OverlayBackgroundDropdown_SelectionChanged;

            Button chooseCustomBackgroundButton = new()
            {
                Content = "Choose Custom Background",
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            chooseCustomBackgroundButton.Click += (_, _) => _main.ChooseCustomBackgroundFromOverlay(this);

            StackPanel content = new()
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            content.Children.Add(CreateLabeledToggleRow("Explosion Overlay", explosionOverlayButton));
            content.Children.Add(CreateLabeledToggleRow("Track Explo Resets", explosionTrackingButton, 12));
            content.Children.Add(CreateFieldLabel("Background", 14));
            content.Children.Add(backgroundDropdown);
            content.Children.Add(chooseCustomBackgroundButton);

            Border root = CreatePanelShell(type, "Launcher Settings", 336, 292, showSettingsButton: true, content, scrollContent: true);
            _launcherSettingsPanel = new LauncherSettingsPanelState
            {
                Root = root,
                ExplosionOverlayButton = explosionOverlayButton,
                ExplosionTrackingButton = explosionTrackingButton,
                BackgroundDropdown = backgroundDropdown,
                ChooseCustomBackgroundButton = chooseCustomBackgroundButton
            };

            return root;
        }

        private Border CreateResetMacrosPanel(GameOverlayComponentType type)
        {
            Button resetMacroButton = CreateToggleButton();
            resetMacroButton.Click += (_, _) => _main.ToggleResetMacroFromOverlay();

            TextBox resetHotkeyBox = new()
            {
                IsReadOnly = true,
                Style = (Style)Resources["OverlayReadOnlyTextBoxStyle"]
            };

            Button setResetHotkeyButton = new()
            {
                Content = "Set",
                Width = 64,
                Background = (Brush)FindResource("OverlayAccentBlueBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"]
            };
            setResetHotkeyButton.Click += OverlaySetResetHotkeyButton_Click;

            ComboBox resetGamemodeDropdown = new()
            {
                Style = (Style)Resources["OverlayComboBoxStyle"],
                ItemsSource = new[]
                {
                    GameMode.Survival.ToString(),
                    GameMode.Hardcore.ToString(),
                    GameMode.Creative.ToString(),
                    GameMode.Freedom.ToString(),
                    GameMode.SaveSlot1.ToString(),
                    GameMode.SaveSlot2.ToString(),
                    GameMode.SaveSlot3.ToString()
                }
            };
            resetGamemodeDropdown.SelectionChanged += OverlayResetGamemodeDropdown_SelectionChanged;

            Button explosionResetButton = CreateToggleButton();
            explosionResetButton.Click += (_, _) => _main.ToggleExplosionResetFromOverlay();

            ComboBox explosionPresetDropdown = new()
            {
                Style = (Style)Resources["OverlayComboBoxStyle"]
            };
            AddPresetItem(explosionPresetDropdown, "46:00 - 46:30", ExplosionResetPreset.Min46_To_4630);
            AddPresetItem(explosionPresetDropdown, "46:00 - 47:00", ExplosionResetPreset.Min46_To_47);
            AddPresetItem(explosionPresetDropdown, "46:00 - 48:00", ExplosionResetPreset.Min46_To_48);
            AddPresetItem(explosionPresetDropdown, "46:00 - 50:00", ExplosionResetPreset.Min46_To_50);
            AddPresetItem(explosionPresetDropdown, "Under 1 Hour", ExplosionResetPreset.Under1Hour);
            AddPresetItem(explosionPresetDropdown, "Over 1 Hour", ExplosionResetPreset.Over1Hour);
            AddPresetItem(explosionPresetDropdown, "Custom", ExplosionResetPreset.Custom);
            explosionPresetDropdown.SelectionChanged += OverlayExplosionPresetDropdown_SelectionChanged;

            TextBox customMinBox = new()
            {
                Width = 74,
                Style = (Style)Resources["OverlayEditableTextBoxStyle"]
            };
            customMinBox.TextChanged += OverlayExplosionCustomRangeBox_TextChanged;
            customMinBox.LostFocus += OverlayExplosionCustomRangeBox_LostFocus;
            customMinBox.PreviewKeyDown += OverlayExplosionCustomRangeBox_PreviewKeyDown;

            TextBox customMaxBox = new()
            {
                Width = 74,
                Style = (Style)Resources["OverlayEditableTextBoxStyle"]
            };
            customMaxBox.TextChanged += OverlayExplosionCustomRangeBox_TextChanged;
            customMaxBox.LostFocus += OverlayExplosionCustomRangeBox_LostFocus;
            customMaxBox.PreviewKeyDown += OverlayExplosionCustomRangeBox_PreviewKeyDown;

            Grid customRangeGrid = new()
            {
                Margin = new Thickness(0, 8, 0, 0)
            };
            customRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customRangeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            customRangeGrid.Children.Add(customMinBox);
            TextBlock arrow = new()
            {
                Text = "→",
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 1);
            customRangeGrid.Children.Add(arrow);
            Grid.SetColumn(customMaxBox, 2);
            customRangeGrid.Children.Add(customMaxBox);

            StackPanel subnauticaOnlySection = new()
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            subnauticaOnlySection.Children.Add(CreateLabeledToggleRow("Explosion Reset", explosionResetButton));
            subnauticaOnlySection.Children.Add(CreateFieldLabel("Explosion Window"));
            explosionPresetDropdown.Margin = new Thickness(0, 4, 0, 0);
            subnauticaOnlySection.Children.Add(explosionPresetDropdown);
            subnauticaOnlySection.Children.Add(customRangeGrid);

            StackPanel content = new()
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            content.Children.Add(CreateLabeledToggleRow("Reset Macro", resetMacroButton));
            content.Children.Add(CreateFieldLabel("Reset Hotkey"));
            content.Children.Add(CreateTextBoxButtonRow(resetHotkeyBox, setResetHotkeyButton));
            content.Children.Add(CreateFieldLabel("Gamemode", topMargin: 10));
            content.Children.Add(resetGamemodeDropdown);
            content.Children.Add(subnauticaOnlySection);

            Border root = CreatePanelShell(type, "Reset Macros", 360, 404, showSettingsButton: true, content, scrollContent: true);
            _resetMacrosPanel = new ResetMacrosPanelState
            {
                Root = root,
                ResetMacroButton = resetMacroButton,
                ResetHotkeyBox = resetHotkeyBox,
                ResetGamemodeDropdown = resetGamemodeDropdown,
                SubnauticaOnlySection = subnauticaOnlySection,
                ExplosionResetButton = explosionResetButton,
                ExplosionPresetDropdown = explosionPresetDropdown,
                ExplosionCustomRangePanel = customRangeGrid,
                ExplosionCustomMinBox = customMinBox,
                ExplosionCustomMaxBox = customMaxBox
            };

            return root;
        }

        private Border CreateOtherToolsPanel(GameOverlayComponentType type)
        {
            Button hardcoreButton = CreateToggleButton();
            hardcoreButton.Click += (_, _) => _main.ToggleHardcoreSaveDeleterFromOverlay();

            Button purgeButton = new()
            {
                Content = "Purge Hardcore Saves",
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            purgeButton.Click += (_, _) => _main.OpenHardcorePurgeFromOverlay(this);

            Button trackerCustomizeButton = new()
            {
                Content = "Customize 100% Tracker",
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"]
            };
            trackerCustomizeButton.Click += (_, _) => _main.OpenTrackerCustomizeFromOverlay(this);

            Button timerCustomizeButton = new()
            {
                Content = "Customize Speedrun Timer",
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Margin = new Thickness(0, 8, 0, 0)
            };
            timerCustomizeButton.Click += (_, _) => _main.OpenSpeedrunTimerCustomizeFromOverlay(this);

            StackPanel trackerButtonsPanel = new()
            {
                Margin = new Thickness(0, 12, 0, 0)
            };
            trackerButtonsPanel.Children.Add(trackerCustomizeButton);
            trackerButtonsPanel.Children.Add(timerCustomizeButton);

            StackPanel content = new()
            {
                Margin = new Thickness(0, 2, 0, 0)
            };
            content.Children.Add(CreateLabeledToggleRow("Hardcore Save Deleter", hardcoreButton));
            content.Children.Add(purgeButton);
            content.Children.Add(CreateFieldLabel("Tracker / Timer", 14));
            content.Children.Add(trackerButtonsPanel);

            Border root = CreatePanelShell(type, "Other Tools", 336, 332, showSettingsButton: true, content, scrollContent: true);
            _otherToolsPanel = new OtherToolsPanelState
            {
                Root = root,
                HardcoreButton = hardcoreButton,
                TrackerButtonsPanel = trackerButtonsPanel
            };

            return root;
        }

        private Border CreateLauncherInfoPanel(GameOverlayComponentType type)
        {
            StackPanel updatesPanel = new();
            ScrollViewer updatesViewer = new()
            {
                Content = updatesPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            StackPanel links = new()
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };

            Button githubButton = new()
            {
                Content = "GitHub",
                Width = 92,
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"]
            };
            githubButton.Click += (_, _) => _main.OpenGitHubFromOverlay();

            Button youtubeButton = new()
            {
                Content = "YouTube",
                Width = 92,
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Margin = new Thickness(8, 0, 0, 0)
            };
            youtubeButton.Click += (_, _) => _main.OpenYouTubeFromOverlay();

            Button discordButton = new()
            {
                Content = "Discord",
                Width = 92,
                Background = (Brush)FindResource("OverlayMainShellButtonBrush"),
                Style = (Style)Resources["OverlayPanelButtonStyle"],
                Margin = new Thickness(8, 0, 0, 0)
            };
            discordButton.Click += (_, _) => _main.OpenDiscordFromOverlay();

            links.Children.Add(githubButton);
            links.Children.Add(youtubeButton);
            links.Children.Add(discordButton);

            Grid content = new();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            content.Children.Add(updatesViewer);
            Grid.SetRow(links, 1);
            content.Children.Add(links);

            Border root = CreatePanelShell(type, "Launcher Info", 390, 390, showSettingsButton: true, content, scrollContent: false);
            _launcherInfoPanel = new LauncherInfoPanelState
            {
                Root = root,
                UpdatesPanel = updatesPanel
            };
            return root;
        }

        private Button CreateToggleButton()
        {
            return new Button
            {
                Width = 118,
                Height = 32,
                Style = (Style)Resources["OverlayPanelButtonStyle"]
            };
        }

        private static void AddPresetItem(ComboBox comboBox, string label, ExplosionResetPreset preset)
        {
            comboBox.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = preset.ToString()
            });
        }

        private static TextBlock CreateFieldLabel(string text, double topMargin = 12)
        {
            return new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, topMargin, 0, 4)
            };
        }

        private static Grid CreateTextBoxButtonRow(TextBox textBox, Button button)
        {
            Grid row = new();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(textBox);
            Grid.SetColumn(button, 1);
            button.Margin = new Thickness(8, 0, 0, 0);
            row.Children.Add(button);
            return row;
        }

        private static StackPanel CreateLabeledToggleRow(string label, Button button, double topMargin = 0)
        {
            StackPanel row = new()
            {
                Margin = new Thickness(0, topMargin, 0, 0)
            };
            row.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            });
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            button.Margin = new Thickness(0, 6, 0, 0);
            row.Children.Add(button);
            return row;
        }

        private Border CreatePanelShell(GameOverlayComponentType type, string title, double width, double height, bool showSettingsButton, UIElement content, bool scrollContent = true)
        {
            Border root = new()
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(16),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 124, 150, 195)),
                BorderThickness = new Thickness(1),
                Background = CreatePanelBrush(_panelOpacity)
            };

            Grid shell = new();
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            Border titleBar = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(175, 18, 25, 40)),
                CornerRadius = new CornerRadius(16, 16, 0, 0),
                Padding = new Thickness(12, 8, 10, 8),
                Tag = type
            };
            titleBar.MouseLeftButtonDown += PanelTitleBar_MouseLeftButtonDown;

            Grid titleGrid = new();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            titleGrid.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            });

            if (showSettingsButton)
            {
                Button settingsButton = new()
                {
                    Content = "⚙",
                    Style = (Style)Resources["OverlayPanelIconButtonStyle"],
                    Tag = type
                };
                settingsButton.Click += OverlayPanelSettingsButton_Click;
                Grid.SetColumn(settingsButton, 1);
                settingsButton.Margin = new Thickness(0, 0, 6, 0);
                titleGrid.Children.Add(settingsButton);
            }

            Button closeButton = new()
            {
                Content = "✕",
                Style = (Style)Resources["OverlayPanelIconButtonStyle"],
                Tag = type
            };
            closeButton.Click += OverlayPanelCloseButton_Click;
            Grid.SetColumn(closeButton, 2);
            titleGrid.Children.Add(closeButton);

            titleBar.Child = titleGrid;
            shell.Children.Add(titleBar);

            UIElement hostContent = content;
            if (scrollContent)
            {
                hostContent = new ScrollViewer
                {
                    Content = content,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
            }

            Border contentHost = new()
            {
                Padding = new Thickness(12),
                Child = hostContent
            };
            Grid.SetRow(contentHost, 1);
            shell.Children.Add(contentHost);

            root.Child = shell;
            return root;
        }

        private SolidColorBrush CreatePanelBrush(double opacity)
        {
            byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.2, 1) * 215);
            return new SolidColorBrush(Color.FromArgb(alpha, 10, 14, 24));
        }

        private void RefreshVersionListPanel(VersionListPanelState panel)
        {
            IReadOnlyList<InstalledVersion> versions = _main.GetVersionsForOverlay(panel.Game);
            string? selectedFolder =
                (panel.ListBox.SelectedItem as InstalledVersion)?.HomeFolder
                ?? (_selectedFoldersByGame.TryGetValue(panel.Game, out string? folder) ? folder : null)
                ?? versions.FirstOrDefault()?.HomeFolder;

            List<InstalledVersion> orderedVersions = versions
                .OrderByDescending(v => v.IsModded)
                .ThenBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            ListCollectionView view = new(orderedVersions);
            bool useLabels = panel.ShowLabels && versions.Any(v => v.IsModded);
            if (useLabels)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(InstalledVersion.GroupLabel)));
            }

            panel.ListBox.GroupStyle.Clear();
            if (useLabels)
            {
                panel.ListBox.GroupStyle.Add(new GroupStyle
                {
                    HeaderTemplate = (DataTemplate)Resources["OverlayVersionHeaderTemplate"]
                });
            }

            panel.ListBox.ItemsSource = view;

            InstalledVersion? selectedVersion = orderedVersions.FirstOrDefault(v => PathsAreEqual(v.HomeFolder, selectedFolder))
                ?? orderedVersions.FirstOrDefault();

            _syncingVersionSelections = true;
            try
            {
                if (selectedVersion != null)
                {
                    panel.ListBox.SelectedItem = selectedVersion;
                    _selectedFoldersByGame[panel.Game] = selectedVersion.HomeFolder;
                }
                else
                {
                    panel.ListBox.SelectedItem = null;
                    _selectedFoldersByGame.Remove(panel.Game);
                }
            }
            finally
            {
                _syncingVersionSelections = false;
            }

            bool anyGameRunning = _main.IsAnyGameRunningForOverlay();
            bool selectedRunning = selectedVersion != null && _main.IsVersionRunningForOverlay(selectedVersion);

            panel.LaunchButton.Content = selectedRunning ? "Close" : "Launch";
            panel.LaunchButton.Background = selectedRunning
                ? (Brush)Application.Current.FindResource("WarningOrangeBrush")
                : (Brush)FindResource("OverlayLaunchAccentBrush");
            panel.LaunchButton.IsEnabled = selectedVersion != null && (selectedRunning || !anyGameRunning);
            panel.SwitchButton.IsEnabled = selectedVersion != null && anyGameRunning && !selectedRunning;
        }

        private void RefreshLauncherSettingsPanel()
        {
            if (_launcherSettingsPanel == null)
                return;

            bool overlayEnabled = _main.IsExplosionOverlayEnabledForOverlay();
            _launcherSettingsPanel.ExplosionOverlayButton.Content = overlayEnabled ? "Enabled" : "Disabled";
            _launcherSettingsPanel.ExplosionOverlayButton.Background = overlayEnabled ? Brushes.Green : Brushes.DarkRed;

            bool trackEnabled = _main.IsExplosionTrackingEnabledForOverlay();
            _launcherSettingsPanel.ExplosionTrackingButton.Content = trackEnabled ? "Enabled" : "Disabled";
            _launcherSettingsPanel.ExplosionTrackingButton.Background = trackEnabled ? Brushes.Green : Brushes.DarkRed;

            string backgroundPreset = _main.GetBackgroundPresetForOverlay();
            _syncingLauncherSettings = true;
            try
            {
                _launcherSettingsPanel.BackgroundDropdown.SelectedItem =
                    _launcherSettingsPanel.BackgroundDropdown.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => string.Equals(item.Content?.ToString(), backgroundPreset, StringComparison.Ordinal))
                    ?? _launcherSettingsPanel.BackgroundDropdown.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(item => string.Equals(item.Content?.ToString(), "Custom", StringComparison.Ordinal) &&
                                                !string.IsNullOrWhiteSpace(backgroundPreset));
            }
            finally
            {
                _syncingLauncherSettings = false;
            }
        }

        private void RefreshResetMacrosPanel()
        {
            if (_resetMacrosPanel == null)
                return;

            bool subnauticaFocused = _currentTargetGame is null or LauncherGame.Subnautica;

            _resetMacrosPanel.ResetMacroButton.Content =
                _main.IsResetMacroEnabledForOverlay() ? "Enabled" : "Disabled";
            _resetMacrosPanel.ResetMacroButton.Background =
                _main.IsResetMacroEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            _resetMacrosPanel.ResetHotkeyBox.Text = _main.GetResetHotkeyForOverlay().ToString();

            _syncingResetGamemode = true;
            try
            {
                string targetMode = _main.GetResetGameModeForOverlay().ToString();
                foreach (object item in _resetMacrosPanel.ResetGamemodeDropdown.Items)
                {
                    if (string.Equals(item?.ToString(), targetMode, StringComparison.Ordinal))
                    {
                        _resetMacrosPanel.ResetGamemodeDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _syncingResetGamemode = false;
            }

            _resetMacrosPanel.SubnauticaOnlySection.Visibility =
                subnauticaFocused ? Visibility.Visible : Visibility.Collapsed;

            if (!subnauticaFocused)
                return;

            _resetMacrosPanel.ExplosionResetButton.Content =
                _main.IsExplosionResetEnabledForOverlay() ? "Enabled" : "Disabled";
            _resetMacrosPanel.ExplosionResetButton.Background =
                _main.IsExplosionResetEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;

            _syncingExplosionPreset = true;
            try
            {
                string presetTag = _main.GetExplosionPresetForOverlay().ToString();
                _resetMacrosPanel.ExplosionPresetDropdown.SelectedItem =
                    _resetMacrosPanel.ExplosionPresetDropdown.Items
                        .OfType<System.Windows.Controls.ComboBoxItem>()
                        .FirstOrDefault(i => string.Equals(i.Tag as string, presetTag, StringComparison.Ordinal));
            }
            finally
            {
                _syncingExplosionPreset = false;
            }

            bool showCustom = _main.IsExplosionCustomPresetForOverlay();
            _resetMacrosPanel.ExplosionCustomRangePanel.Visibility = showCustom ? Visibility.Visible : Visibility.Collapsed;

            _syncingExplosionCustomRange = true;
            try
            {
                _resetMacrosPanel.ExplosionCustomMinBox.Text = _main.GetExplosionCustomMinTextForOverlay();
                _resetMacrosPanel.ExplosionCustomMaxBox.Text = _main.GetExplosionCustomMaxTextForOverlay();
            }
            finally
            {
                _syncingExplosionCustomRange = false;
            }
        }

        private void RefreshOtherToolsPanel()
        {
            if (_otherToolsPanel == null)
                return;

            bool showHardcore = _currentTargetGame is LauncherGame.Subnautica or LauncherGame.BelowZero || _currentTargetGame == null;
            bool showTrackerButtons = _currentTargetGame == LauncherGame.Subnautica || _currentTargetGame == null;

            _otherToolsPanel.HardcoreButton.Content =
                _main.IsHardcoreSaveDeleterEnabledForOverlay() ? "Enabled" : "Disabled";
            _otherToolsPanel.HardcoreButton.Background =
                _main.IsHardcoreSaveDeleterEnabledForOverlay() ? Brushes.Green : Brushes.DarkRed;
            _otherToolsPanel.HardcoreButton.Visibility = showHardcore ? Visibility.Visible : Visibility.Collapsed;
            _otherToolsPanel.TrackerButtonsPanel.Visibility = showTrackerButtons ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshLauncherInfoPanel()
        {
            if (_launcherInfoPanel == null || _launcherInfoPanel.UpdatesPanel.Children.Count > 0)
                return;

            _launcherInfoPanel.UpdatesPanel.Children.Clear();

            foreach (UpdateEntry update in UpdatesData.History)
            {
                Border card = new()
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                StackPanel panel = new();
                panel.Children.Add(new TextBlock
                {
                    Text = $"{update.Version} ({update.Title})",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap
                });
                panel.Children.Add(new TextBlock
                {
                    Text = update.Date,
                    FontSize = 11,
                    Foreground = Brushes.Gainsboro,
                    Margin = new Thickness(0, 2, 0, 4),
                    TextWrapping = TextWrapping.Wrap
                });

                foreach (string change in update.Changes)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "- " + change,
                        FontSize = 11,
                        Foreground = Brushes.Gainsboro,
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                card.Child = panel;
                _launcherInfoPanel.UpdatesPanel.Children.Add(card);
            }

        }

        private void UpdateTopBarText()
        {
            InstalledVersion? selectedVersion = GetPreferredSelectedVersion();
            InstalledVersion? activeVersion = GetPreferredActiveVersion();

            SelectedVersionValueText.Text = selectedVersion?.DisplayLabel ?? "None";
            SelectedVersionValueText.Foreground = GetStatusBrush(selectedVersion?.Status ?? VersionStatus.Idle);
            ActiveVersionValueText.Text = activeVersion?.DisplayLabel ?? "None";
            ActiveVersionValueText.Foreground = GetStatusBrush(activeVersion?.Status ?? VersionStatus.Idle);
        }

        private InstalledVersion? GetPreferredSelectedVersion()
        {
            if (_preferredSelectedGame.HasValue)
            {
                InstalledVersion? preferred = GetPanelSelectedVersion(_preferredSelectedGame.Value);
                if (preferred != null)
                    return preferred;
            }

            if (_currentTargetGame.HasValue)
            {
                InstalledVersion? current = GetPanelSelectedVersion(_currentTargetGame.Value);
                if (current != null)
                    return current;
            }

            foreach (LauncherGame game in Enum.GetValues<LauncherGame>())
            {
                InstalledVersion? version = GetPanelSelectedVersion(game);
                if (version != null)
                    return version;
            }

            return null;
        }

        private InstalledVersion? GetPreferredActiveVersion()
        {
            return Enum.GetValues<LauncherGame>()
                .Select(_main.GetActiveVersionForOverlay)
                .Where(v => v != null)
                .OrderByDescending(v => GetStatusPriority(v!.Status))
                .FirstOrDefault();
        }

        private InstalledVersion? GetSelectedVersionForGame(LauncherGame game)
        {
            if (!_selectedFoldersByGame.TryGetValue(game, out string? folder))
                return null;

            return _main.GetVersionsForOverlay(game)
                .FirstOrDefault(v => PathsAreEqual(v.HomeFolder, folder));
        }

        private InstalledVersion? GetPanelSelectedVersion(LauncherGame game)
        {
            VersionListPanelState? panel = _versionPanels.Values.FirstOrDefault(p => p.Game == game);
            if (panel?.ListBox.SelectedItem is InstalledVersion selected)
                return selected;

            return GetSelectedVersionForGame(game);
        }

        private static int GetStatusPriority(VersionStatus status)
        {
            return status switch
            {
                VersionStatus.Launching => 5,
                VersionStatus.Launched => 4,
                VersionStatus.Closing => 3,
                VersionStatus.Switching => 2,
                VersionStatus.Active => 1,
                _ => 0
            };
        }

        private static bool PathsAreEqual(string? left, string? right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(
                       Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                       Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase);
        }

        private void AddComponentButton_Click(object sender, RoutedEventArgs e)
        {
            List<GameOverlayComponentType> available = Enum.GetValues<GameOverlayComponentType>()
                .Where(type => !_layouts.ContainsKey(type))
                .ToList();

            if (available.Count == 0)
                return;

            ContextMenu menu = new();
            foreach (GameOverlayComponentType type in available)
            {
                MenuItem item = new()
                {
                    Header = GetComponentTitle(type),
                    Tag = type
                };
                item.Click += AddComponentMenuItem_Click;
                menu.Items.Add(item);
            }

            AddComponentButton.ContextMenu = menu;
            menu.PlacementTarget = AddComponentButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void AddComponentMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem { Tag: GameOverlayComponentType type })
                return;

            Point defaultPosition = FindAvailablePosition(type);
            _layouts[type] = new GameOverlayComponentLayout
            {
                Type = type,
                Left = defaultPosition.X,
                Top = defaultPosition.Y,
                ShowLabels = true
            };
            SaveLayouts();
            SyncPanelsToLayouts();
            RefreshFromMain();
        }

        private void OverlaySettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = new();

            MenuItem currentHotkey = new()
            {
                Header = $"Overlay Toggle Hotkey: {_main.GetOverlayHotkeyTextForOverlay()}",
                IsEnabled = false
            };
            menu.Items.Add(currentHotkey);

            MenuItem setHotkey = new() { Header = "Set Overlay Toggle Hotkey" };
            setHotkey.Click += (_, _) =>
            {
                BeginOverlayHotkeyCapture();
            };
            menu.Items.Add(setHotkey);
            menu.Items.Add(new Separator());

            MenuItem resetLayout = new() { Header = "Reset Visible Layout" };
            resetLayout.Click += (_, _) =>
            {
                foreach ((GameOverlayComponentType type, GameOverlayComponentLayout layout) in _layouts)
                {
                    Point point = FindAvailablePosition(type, preferDefaults: true, ignoreCurrent: type);
                    layout.Left = point.X;
                    layout.Top = point.Y;
                }

                SaveLayouts();
                SyncPanelsToLayouts();
            };
            menu.Items.Add(resetLayout);

            MenuItem clearPanels = new() { Header = "Remove All Panels" };
            clearPanels.Click += (_, _) =>
            {
                _layouts.Clear();
                SaveLayouts();
                SyncPanelsToLayouts();
            };
            menu.Items.Add(clearPanels);

            menu.Items.Add(new Separator());

            MenuItem closeOverlay = new() { Header = "Hide Overlay" };
            closeOverlay.Click += (_, _) => Hide();
            menu.Items.Add(closeOverlay);

            OverlaySettingsButton.ContextMenu = menu;
            menu.PlacementTarget = OverlaySettingsButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OverlayPanelCloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: GameOverlayComponentType type })
                return;

            _layouts.Remove(type);
            SaveLayouts();
            SyncPanelsToLayouts();
            UpdateTopBarText();
        }

        private void OverlayPanelSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: GameOverlayComponentType type })
                return;

            ContextMenu menu = new();

            if (_versionPanels.TryGetValue(type, out VersionListPanelState? panel))
            {
                MenuItem labeled = new()
                {
                    Header = "Labeled",
                    IsCheckable = true,
                    IsChecked = panel.ShowLabels
                };
                labeled.Click += (_, _) => SetVersionListLabels(type, true);

                MenuItem noLabels = new()
                {
                    Header = "Not Labeled",
                    IsCheckable = true,
                    IsChecked = !panel.ShowLabels
                };
                noLabels.Click += (_, _) => SetVersionListLabels(type, false);

                menu.Items.Add(labeled);
                menu.Items.Add(noLabels);
                menu.Items.Add(new Separator());
            }

            menu.Items.Add(new MenuItem
            {
                Header = "Panel Opacity",
                IsEnabled = false
            });

            StackPanel opacityPanel = new()
            {
                Margin = new Thickness(10, 4, 10, 8)
            };
            opacityPanel.Children.Add(new TextBlock
            {
                Text = "Use Global / Per Panel",
                FontSize = 11,
                Foreground = Brushes.Black,
                Margin = new Thickness(0, 0, 0, 6)
            });

            CheckBox useGlobalCheckBox = new()
            {
                Content = "Use global opacity",
                Foreground = Brushes.Black,
                IsChecked = !_layouts.TryGetValue(type, out GameOverlayComponentLayout? existingLayout) || existingLayout.PanelOpacity is null,
                Margin = new Thickness(0, 0, 0, 6)
            };

            Slider opacitySlider = new()
            {
                Minimum = 0.2,
                Maximum = 1.0,
                TickFrequency = 0.05,
                IsSnapToTickEnabled = true,
                Width = 170,
                Value = _layouts.TryGetValue(type, out GameOverlayComponentLayout? sliderLayout) && sliderLayout.PanelOpacity is double panelOpacity
                    ? Math.Clamp(panelOpacity, 0.2, 1)
                    : _panelOpacity,
                IsEnabled = useGlobalCheckBox.IsChecked != true
            };

            TextBlock opacityValueText = new()
            {
                Text = $"{(int)Math.Round(opacitySlider.Value * 100)}%",
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Foreground = Brushes.Black
            };

            useGlobalCheckBox.Checked += (_, _) =>
            {
                opacitySlider.IsEnabled = false;
                ApplyPanelOpacityOverride(type, null);
                opacityValueText.Text = $"{(int)Math.Round(_panelOpacity * 100)}%";
            };
            useGlobalCheckBox.Unchecked += (_, _) =>
            {
                opacitySlider.IsEnabled = true;
                ApplyPanelOpacityOverride(type, opacitySlider.Value);
                opacityValueText.Text = $"{(int)Math.Round(opacitySlider.Value * 100)}%";
            };
            opacitySlider.ValueChanged += (_, args) =>
            {
                if (useGlobalCheckBox.IsChecked == true)
                    return;

                ApplyPanelOpacityOverride(type, args.NewValue);
                opacityValueText.Text = $"{(int)Math.Round(args.NewValue * 100)}%";
            };

            opacityPanel.Children.Add(useGlobalCheckBox);
            opacityPanel.Children.Add(opacitySlider);
            opacityPanel.Children.Add(opacityValueText);

            MenuItem opacityHost = new()
            {
                StaysOpenOnClick = true,
                Header = opacityPanel
            };
            menu.Items.Add(opacityHost);

            if (sender is Button button)
            {
                button.ContextMenu = menu;
                menu.PlacementTarget = button;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void SetVersionListLabels(GameOverlayComponentType type, bool showLabels)
        {
            if (!_layouts.TryGetValue(type, out GameOverlayComponentLayout? layout))
                return;

            layout.ShowLabels = showLabels;
            if (_versionPanels.TryGetValue(type, out VersionListPanelState? panel))
                panel.ShowLabels = showLabels;

            SaveLayouts();
            RefreshVersionStatusOnly();
        }

        private void ApplyPanelOpacityOverride(GameOverlayComponentType type, double? opacity)
        {
            if (!_layouts.TryGetValue(type, out GameOverlayComponentLayout? layout))
                return;

            layout.PanelOpacity = opacity;
            if (_componentRoots.TryGetValue(type, out Border? root))
                root.Background = CreatePanelBrush(GetPanelOpacity(layout));

            SaveLayouts();
        }

        private double GetPanelOpacity(GameOverlayComponentLayout layout)
        {
            if (layout.PanelOpacity is double panelOpacity && !double.IsNaN(panelOpacity))
                return Math.Clamp(panelOpacity, 0.2, 1);

            return _panelOpacity;
        }

        private Point FindClosestAvailablePosition(GameOverlayComponentType type, Point preferred)
        {
            (double width, double height) = GetDefaultPanelSize(type);
            Point fallback = FindAvailablePosition(type, preferDefaults: false, ignoreCurrent: type);
            Point? best = null;
            double bestDistance = double.MaxValue;

            for (double top = TopMargin; top <= Math.Max(TopMargin, OverlayCanvas.Height - height - 12); top += 18)
            {
                for (double left = 12; left <= Math.Max(12, OverlayCanvas.Width - width - 12); left += 18)
                {
                    Rect rect = new(left, top, width, height);
                    bool overlaps = _componentRoots.Any(entry =>
                    {
                        if (entry.Key == type)
                            return false;

                        (double otherWidth, double otherHeight) = GetPanelSize(entry.Value);
                        Rect other = new(Canvas.GetLeft(entry.Value), Canvas.GetTop(entry.Value), otherWidth, otherHeight);
                        return rect.IntersectsWith(other);
                    });

                    if (overlaps)
                        continue;

                    double distance = Math.Pow(left - preferred.X, 2) + Math.Pow(top - preferred.Y, 2);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Point(left, top);
                    }
                }
            }

            return best ?? fallback;
        }

        private static Brush GetStatusBrush(VersionStatus status)
        {
            return status switch
            {
                VersionStatus.Active => Brushes.LimeGreen,
                VersionStatus.Launched => Brushes.Red,
                VersionStatus.Launching => Brushes.Orange,
                VersionStatus.Switching => Brushes.Yellow,
                VersionStatus.Closing => Brushes.OrangeRed,
                _ => Brushes.White
            };
        }

        private void PanelTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: GameOverlayComponentType type })
                return;

            if (!_componentRoots.TryGetValue(type, out Border? root))
                return;

            _draggingType = type;
            _dragStartMouse = e.GetPosition(RootGrid);
            _dragStartLeft = Canvas.GetLeft(root);
            _dragStartTop = Canvas.GetTop(root);
            Mouse.Capture(RootGrid);
            e.Handled = true;
        }

        private void RootGrid_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingType is not GameOverlayComponentType type || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point current = e.GetPosition(RootGrid);
            double candidateLeft = _dragStartLeft + (current.X - _dragStartMouse.X);
            double candidateTop = _dragStartTop + (current.Y - _dragStartMouse.Y);

            ApplyPanelPosition(type, candidateLeft, candidateTop);
        }

        private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingType == null)
                return;

            Mouse.Capture(null);
            if (_draggingType is GameOverlayComponentType type &&
                _componentRoots.TryGetValue(type, out Border? root) &&
                _layouts.TryGetValue(type, out GameOverlayComponentLayout? layout))
            {
                ResolveAndPersistComponentPosition(type, Canvas.GetLeft(root), Canvas.GetTop(root));
                SaveLayouts();
            }

            _draggingType = null;
        }

        private bool TryMoveComponent(GameOverlayComponentType type, double left, double top, bool persist)
        {
            if (!_componentRoots.TryGetValue(type, out Border? root))
                return false;

            (double width, double height) = GetPanelSize(root);
            double clampedLeft = Math.Clamp(left, 12, Math.Max(12, OverlayCanvas.Width - width - 12));
            double clampedTop = Math.Clamp(top, TopMargin, Math.Max(TopMargin, OverlayCanvas.Height - height - 12));
            Rect proposed = new(clampedLeft, clampedTop, width, height);

            foreach ((GameOverlayComponentType otherType, Border otherRoot) in _componentRoots)
            {
                if (otherType == type)
                    continue;

                (double otherWidth, double otherHeight) = GetPanelSize(otherRoot);
                Rect otherRect = new(Canvas.GetLeft(otherRoot), Canvas.GetTop(otherRoot), otherWidth, otherHeight);
                if (proposed.IntersectsWith(otherRect))
                    return false;
            }

            ApplyPanelPosition(type, clampedLeft, clampedTop);
            if (persist && _layouts.TryGetValue(type, out GameOverlayComponentLayout? layout))
            {
                layout.Left = clampedLeft;
                layout.Top = clampedTop;
                SaveLayouts();
            }

            return true;
        }

        private void ResolveAndPersistComponentPosition(GameOverlayComponentType type, double left, double top)
        {
            if (!_componentRoots.TryGetValue(type, out Border? root) ||
                !_layouts.TryGetValue(type, out GameOverlayComponentLayout? layout))
            {
                return;
            }

            (double width, double height) = GetPanelSize(root);
            double clampedLeft = Math.Clamp(left, 12, Math.Max(12, OverlayCanvas.Width - width - 12));
            double clampedTop = Math.Clamp(top, TopMargin, Math.Max(TopMargin, OverlayCanvas.Height - height - 12));
            Rect proposed = new(clampedLeft, clampedTop, width, height);

            bool overlaps = _componentRoots.Any(entry =>
            {
                if (entry.Key == type)
                    return false;

                (double otherWidth, double otherHeight) = GetPanelSize(entry.Value);
                Rect other = new(Canvas.GetLeft(entry.Value), Canvas.GetTop(entry.Value), otherWidth, otherHeight);
                return proposed.IntersectsWith(other);
            });

            Point finalPoint = overlaps
                ? FindClosestAvailablePosition(type, new Point(clampedLeft, clampedTop))
                : new Point(clampedLeft, clampedTop);

            layout.Left = finalPoint.X;
            layout.Top = finalPoint.Y;
            ApplyPanelPosition(type, finalPoint.X, finalPoint.Y);
        }

        private void ClampAllPanelsToBounds(bool persist)
        {
            foreach (GameOverlayComponentType type in _componentRoots.Keys.ToList())
            {
                if (!_componentRoots.TryGetValue(type, out Border? root))
                    continue;

                if (persist)
                {
                    ResolveAndPersistComponentPosition(type, Canvas.GetLeft(root), Canvas.GetTop(root));
                    continue;
                }

                Point resolved = ResolveComponentPosition(type, Canvas.GetLeft(root), Canvas.GetTop(root));
                ApplyPanelPosition(type, resolved.X, resolved.Y);
            }

            if (persist)
                SaveLayouts();
        }

        private Point ResolveComponentPosition(GameOverlayComponentType type, double left, double top)
        {
            if (!_componentRoots.TryGetValue(type, out Border? root))
                return new Point(left, top);

            (double width, double height) = GetPanelSize(root);
            double clampedLeft = Math.Clamp(left, 12, Math.Max(12, OverlayCanvas.Width - width - 12));
            double clampedTop = Math.Clamp(top, TopMargin, Math.Max(TopMargin, OverlayCanvas.Height - height - 12));
            Rect proposed = new(clampedLeft, clampedTop, width, height);

            bool overlaps = _componentRoots.Any(entry =>
            {
                if (entry.Key == type)
                    return false;

                (double otherWidth, double otherHeight) = GetPanelSize(entry.Value);
                Rect other = new(Canvas.GetLeft(entry.Value), Canvas.GetTop(entry.Value), otherWidth, otherHeight);
                return proposed.IntersectsWith(other);
            });

            return overlaps
                ? FindClosestAvailablePosition(type, new Point(clampedLeft, clampedTop))
                : new Point(clampedLeft, clampedTop);
        }

        private void ApplyPanelPosition(GameOverlayComponentType type, double left, double top)
        {
            if (_componentRoots.TryGetValue(type, out Border? root))
            {
                Canvas.SetLeft(root, left);
                Canvas.SetTop(root, top);
            }
        }

        private (double Width, double Height) GetPanelSize(Border root)
        {
            double width = root.ActualWidth > 0 ? root.ActualWidth : root.Width;
            double height = root.ActualHeight > 0 ? root.ActualHeight : root.Height;
            return (width, height);
        }

        private Point FindAvailablePosition(GameOverlayComponentType type, bool preferDefaults = true, GameOverlayComponentType? ignoreCurrent = null)
        {
            (double width, double height) = GetDefaultPanelSize(type);
            Point preferred = preferDefaults ? GetDefaultPosition(type) : new Point(24, TopMargin);
            double maxLeft = Math.Max(12, OverlayCanvas.Width - width - 12);
            double maxTop = Math.Max(TopMargin, OverlayCanvas.Height - height - 12);
            preferred = new Point(
                Math.Clamp(preferred.X, 12, maxLeft),
                Math.Clamp(preferred.Y, TopMargin, maxTop));

            for (double top = preferred.Y; top <= maxTop; top += 18)
            {
                for (double left = preferred.X; left <= maxLeft; left += 18)
                {
                    Rect rect = new(left, top, width, height);
                    bool overlaps = _componentRoots.Any(entry =>
                    {
                        if (ignoreCurrent.HasValue && entry.Key == ignoreCurrent.Value)
                            return false;

                        (double otherWidth, double otherHeight) = GetPanelSize(entry.Value);
                        Rect other = new(Canvas.GetLeft(entry.Value), Canvas.GetTop(entry.Value), otherWidth, otherHeight);
                        return rect.IntersectsWith(other);
                    });

                    if (!overlaps)
                        return new Point(left, top);
                }
            }

            for (double top = TopMargin; top <= maxTop; top += 18)
            {
                for (double left = 12; left <= maxLeft; left += 18)
                {
                    Rect rect = new(left, top, width, height);
                    bool overlaps = _componentRoots.Any(entry =>
                    {
                        if (ignoreCurrent.HasValue && entry.Key == ignoreCurrent.Value)
                            return false;

                        (double otherWidth, double otherHeight) = GetPanelSize(entry.Value);
                        Rect other = new(Canvas.GetLeft(entry.Value), Canvas.GetTop(entry.Value), otherWidth, otherHeight);
                        return rect.IntersectsWith(other);
                    });

                    if (!overlaps)
                        return new Point(left, top);
                }
            }

            return new Point(Math.Clamp(preferred.X, 12, maxLeft), Math.Clamp(preferred.Y, TopMargin, maxTop));
        }

        private static (double Width, double Height) GetDefaultPanelSize(GameOverlayComponentType type)
        {
            return type switch
            {
                GameOverlayComponentType.LauncherSettings => (336, 292),
                GameOverlayComponentType.ResetMacros => (360, 404),
                GameOverlayComponentType.OtherTools => (336, 332),
                GameOverlayComponentType.LauncherInfo => (390, 390),
                _ => (360, 470)
            };
        }

        private static Point GetDefaultPosition(GameOverlayComponentType type)
        {
            return type switch
            {
                GameOverlayComponentType.SubnauticaVersionList => new Point(24, TopMargin),
                GameOverlayComponentType.BelowZeroVersionList => new Point(404, TopMargin),
                GameOverlayComponentType.Subnautica2VersionList => new Point(784, TopMargin),
                GameOverlayComponentType.LauncherSettings => new Point(24, TopMargin),
                GameOverlayComponentType.ResetMacros => new Point(404, TopMargin),
                GameOverlayComponentType.OtherTools => new Point(784, TopMargin),
                GameOverlayComponentType.LauncherInfo => new Point(24, TopMargin),
                _ => new Point(24, TopMargin)
            };
        }

        private static string GetComponentTitle(GameOverlayComponentType type)
        {
            return type switch
            {
                GameOverlayComponentType.SubnauticaVersionList => "Subnautica Version List",
                GameOverlayComponentType.BelowZeroVersionList => "Below Zero Version List",
                GameOverlayComponentType.Subnautica2VersionList => "Subnautica 2 Version List",
                GameOverlayComponentType.LauncherSettings => "Launcher Settings",
                GameOverlayComponentType.ResetMacros => "Reset Macros",
                GameOverlayComponentType.OtherTools => "Other Tools",
                _ => "Launcher Info"
            };
        }

        private void OverlayVersionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingVersionSelections)
                return;

            if (sender is not ListBox { Tag: LauncherGame game, SelectedItem: InstalledVersion version })
                return;

            _preferredSelectedGame = game;
            _selectedFoldersByGame[game] = version.HomeFolder;
            UpdateTopBarText();
        }

        private async void OverlayLaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: LauncherGame game })
                return;

            _preferredSelectedGame = game;
            _main.RefreshRunningStateForOverlay();
            RefreshVersionStatusOnly();

            InstalledVersion? selectedVersion = GetPanelSelectedVersion(game);
            if (selectedVersion == null)
                return;

            bool selectedRunning = _main.IsVersionRunningForOverlay(selectedVersion);
            if (selectedRunning)
                await _main.CloseGameForOverlayAsync();
            else
                await _main.LaunchVersionFromOverlayAsync(selectedVersion);
        }

        private async void OverlaySwitchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: LauncherGame game })
                return;

            _preferredSelectedGame = game;
            _main.RefreshRunningStateForOverlay();
            RefreshVersionStatusOnly();

            InstalledVersion? selectedVersion = GetPanelSelectedVersion(game);
            if (selectedVersion == null)
                return;

            await _main.LaunchVersionFromOverlayAsync(selectedVersion);
        }

        private async void EditOverlayVersion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: InstalledVersion version })
                await _main.EditVersionFromOverlayAsync(this, version);
        }

        private void OpenOverlayVersionFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: InstalledVersion version })
                _main.OpenVersionFolderFromOverlay(version);
        }

        private void OverlaySetResetHotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            BeginResetHotkeyCapture();
        }

        private void LauncherOverlayWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingOverlayHotkey && !_capturingResetHotkey)
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftShift or Key.RightShift or
                Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or
                Key.LWin or Key.RWin)
            {
                return;
            }

            if (_capturingOverlayHotkey)
            {
                ModifierKeys modifiers = Keyboard.Modifiers;
                if (modifiers == ModifierKeys.None)
                    modifiers = ModifierKeys.Control | ModifierKeys.Shift;

                _main.SetOverlayHotkeyFromOverlay(modifiers, key);
                _capturingOverlayHotkey = false;
            }
            else if (_capturingResetHotkey)
            {
                _main.SetResetHotkeyFromOverlay(key);
                _capturingResetHotkey = false;
            }

            RefreshFromMain();
            e.Handled = true;
        }

        private void BeginOverlayHotkeyCapture()
        {
            _capturingOverlayHotkey = true;
            _capturingResetHotkey = false;
            SelectedVersionValueText.Text = "Press keys...";
            PrepareForKeyCapture();
        }

        private void BeginResetHotkeyCapture()
        {
            _capturingResetHotkey = true;
            _capturingOverlayHotkey = false;
            if (_resetMacrosPanel != null)
                _resetMacrosPanel.ResetHotkeyBox.Text = "Press a key...";
            PrepareForKeyCapture();
        }

        private void PrepareForKeyCapture()
        {
            Dispatcher.BeginInvoke(() =>
            {
                Activate();
                Focus();
                Keyboard.Focus(RootGrid);
                FocusManager.SetFocusedElement(this, RootGrid);
            }, DispatcherPriority.Input);
        }

        private void OverlayResetGamemodeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingResetGamemode || _resetMacrosPanel?.ResetGamemodeDropdown.SelectedItem is not string selectedText)
                return;

            if (Enum.TryParse(selectedText, out GameMode mode))
                _main.SetResetGameModeFromOverlay(mode);
        }

        private void OverlayExplosionPresetDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingExplosionPreset ||
                _resetMacrosPanel?.ExplosionPresetDropdown.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string tag ||
                !Enum.TryParse(tag, out ExplosionResetPreset preset))
            {
                return;
            }

            _main.SetExplosionPresetFromOverlay(preset);
            RefreshResetMacrosPanel();
        }

        private void OverlayExplosionCustomRangeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncingExplosionCustomRange || _resetMacrosPanel == null)
                return;

            _main.TrySetExplosionCustomRangeFromOverlay(
                _resetMacrosPanel.ExplosionCustomMinBox.Text,
                _resetMacrosPanel.ExplosionCustomMaxBox.Text);
        }

        private void OverlayExplosionCustomRangeBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_syncingExplosionCustomRange || _resetMacrosPanel == null)
                return;

            _main.CommitExplosionCustomRangeFromOverlay(
                _resetMacrosPanel.ExplosionCustomMinBox.Text,
                _resetMacrosPanel.ExplosionCustomMaxBox.Text);
        }

        private void OverlayExplosionCustomRangeBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _resetMacrosPanel == null)
                return;

            e.Handled = true;
            _main.CommitExplosionCustomRangeFromOverlay(
                _resetMacrosPanel.ExplosionCustomMinBox.Text,
                _resetMacrosPanel.ExplosionCustomMaxBox.Text);
            Keyboard.ClearFocus();
        }

        private void OverlayBackgroundDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingLauncherSettings)
                return;

            if (_launcherSettingsPanel?.BackgroundDropdown.SelectedItem is not ComboBoxItem item ||
                item.Content is not string preset)
            {
                return;
            }

            if (string.Equals(preset, "Custom", StringComparison.Ordinal))
                return;

            _main.SetBackgroundPresetFromOverlay(preset);
        }

        private bool ShouldClearLegacyDefaultLayout(List<GameOverlayComponentLayout> layouts)
        {
            if (layouts.Count != 6)
                return false;

            HashSet<GameOverlayComponentType> expectedTypes =
            [
                GameOverlayComponentType.SubnauticaVersionList,
                GameOverlayComponentType.BelowZeroVersionList,
                GameOverlayComponentType.Subnautica2VersionList,
                GameOverlayComponentType.LauncherSettings,
                GameOverlayComponentType.ResetMacros,
                GameOverlayComponentType.LauncherInfo
            ];

            if (layouts.Any(layout => !expectedTypes.Contains(layout.Type)))
                return false;

            return layouts.All(layout =>
            {
                Point expected = GetDefaultPosition(layout.Type);
                return Math.Abs(layout.Left - expected.X) < 1 && Math.Abs(layout.Top - expected.Y) < 1;
            });
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, int uCmd);

        private static string GetWindowClassName(IntPtr hWnd)
        {
            StringBuilder builder = new(256);
            _ = GetClassName(hWnd, builder, builder.Capacity);
            return builder.ToString();
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }
    }
}
