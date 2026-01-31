using SubnauticaLauncher.Macros;
using SubnauticaLauncher.Versions;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace SubnauticaLauncher.Explosion
{
    public partial class ExplosionTimeWindow : Window
    {
        private readonly DispatcherTimer _timer;

        public ExplosionTimeWindow()
        {
            InitializeComponent();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // 10 Hz
            };

            _timer.Tick += OnTick;
            _timer.Start();

            Closed += (_, _) => _timer.Stop();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            var proc = Process.GetProcessesByName("Subnautica").FirstOrDefault();
            if (proc == null)
            {
                StatusText.Text = "Subnautica not running";
                ExplosionTimeText.Text = "--.- s";
                CoordsText.Text = "X: ---.---  Y: ---.---  Z: ---.---";
                return;
            }

            try
            {
                string root = Path.GetDirectoryName(proc.MainModule!.FileName!)!;
                int yearGroup = BuildYearResolver.ResolveGroupedYear(root);

                var resolver = ExplosionResolverFactory.Get(yearGroup);
                resolver.TryRead(proc, out ExplosionSnapshot snapshot);

                StatusText.Text =
                    yearGroup <= 2021
                        ? "2018 Patch detected"
                        : "2023 Patch detected";

                ExplosionTimeText.Text =
                    snapshot.ExplosionTime >= 0
                        ? $"{snapshot.ExplosionTime:F2} s"
                        : "--.- s";

                CoordsText.Text =
                    $"X: {snapshot.PosX:F3}   Y: {snapshot.PosY:F3}   Z: {snapshot.PosZ:F3}";
            }
            catch
            {
                StatusText.Text = "Read failed";
                ExplosionTimeText.Text = "--.- s";
                CoordsText.Text = "X: ---.---  Y: ---.---  Z: ---.---";
            }
        }
    }
}