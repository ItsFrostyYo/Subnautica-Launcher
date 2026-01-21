using System.IO;
using System.Windows;

namespace SubnauticaLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Ensure DepotDownloader exists
            if (!File.Exists(AppPaths.DepotDownloaderExe))
            {
                var setup = new SetupWindow();
                setup.ShowDialog();

                // If setup failed or was closed
                if (!File.Exists(AppPaths.DepotDownloaderExe))
                {
                    Shutdown();
                    return;
                }
            }

            var main = new MainWindow();
            main.Show();
        }
    }
}