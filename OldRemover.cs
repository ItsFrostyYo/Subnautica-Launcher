using System.IO;

namespace SubnauticaLauncher
{
    public static class OldRemover
    {
        /// <summary>
        /// Cleans up files and folders from older launcher versions.
        /// Safe to call every startup.
        /// </summary>
        public static void Run()
        {
            RemoveOldBackgroundsFolder();
        }

        // =========================
        // BACKGROUND CLEANUP (OLD VERSIONS)
        // =========================
        private static void RemoveOldBackgroundsFolder()
        {
            string oldBackgroundsPath =
                Path.Combine(AppPaths.DataPath, "backgrounds");

            if (!Directory.Exists(oldBackgroundsPath))
                return;

            try
            {
                Directory.Delete(oldBackgroundsPath, recursive: true);
            }
            catch
            {
                // Never block launcher startup
            }
        }
    }
}