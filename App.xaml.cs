using System.IO;
using System.Windows;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace SubnauticaLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
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