using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using System;

namespace SubnauticaLauncher
{
    public partial class App : Application
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 =
            new IntPtr(-4);

        protected override void OnStartup(StartupEventArgs e)
        {
            // 🔥 MUST be first
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    ex.Exception.ToString(),
                    "UNHANDLED WPF EXCEPTION",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                ex.Handled = true;
            };

            base.OnStartup(e);
        }
    }
}
