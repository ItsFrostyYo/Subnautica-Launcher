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

            string oldPresetPath =
                Path.Combine(oldBackgroundsPath, "BPreset.txt");

            string newPresetPath =
                Path.Combine(AppPaths.DataPath, "BPreset.txt");

            if (!Directory.Exists(oldBackgroundsPath))
                return;

            try
            {
                // ✅ If old preset exists, it ALWAYS wins
                if (File.Exists(oldPresetPath))
                {
                    Directory.CreateDirectory(AppPaths.DataPath);

                    File.Copy(
                        oldPresetPath,
                        newPresetPath,
                        overwrite: true // 🔥 IMPORTANT FIX
                    );
                }

                // Remove obsolete folder
                Directory.Delete(oldBackgroundsPath, recursive: true);
            }
            catch
            {
                // Never block launcher startup
            }
        }
    }
}