using SubnauticaLauncher.Gameplay;
using System;
using System.Collections.Generic;
using System.Windows;

namespace SubnauticaLauncher.UI
{
    public partial class DebugTelemetryWindow : Window
    {
        private readonly Queue<string> _eventLines = new();
        private const int MaxLines = 350;

        public DebugTelemetryWindow()
        {
            InitializeComponent();
            Left = 20;
            Top = 20;
        }

        public void SetState(string text)
        {
            StateText.Text = $"State: {text}";
        }

        public void SetExplosionTime(double? explosionTimeSeconds)
        {
            if (!explosionTimeSeconds.HasValue || explosionTimeSeconds.Value < 0d)
            {
                ExplosionTimeText.Text = "Explosion Time: n/a";
                return;
            }

            var ts = TimeSpan.FromSeconds(explosionTimeSeconds.Value);
            ExplosionTimeText.Text = $"Explosion Time: {(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        public void SetPosition(float? x, float? y, float? z)
        {
            if (!x.HasValue || !y.HasValue || !z.HasValue)
            {
                PositionText.Text = "Position: n/a";
                return;
            }

            PositionText.Text = $"Position: X={x.Value:0.000}, Y={y.Value:0.000}, Z={z.Value:0.000}";
        }

        public void SetProcessText(string text)
        {
            ProcessText.Text = $"Process: {text}";
        }

        public void AppendEvent(GameplayEvent evt)
        {
            string line =
                $"[{evt.TimestampUtc:HH:mm:ss.fff}] [{evt.Game}] {evt.Type} key={evt.Key} delta={evt.Delta} src={evt.Source}";

            _eventLines.Enqueue(line);
            while (_eventLines.Count > MaxLines)
                _eventLines.Dequeue();

            EventsTextBox.Text = string.Join(Environment.NewLine, _eventLines);
            EventsTextBox.ScrollToEnd();
        }
    }
}
