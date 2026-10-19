using System.IO;

namespace SubnauticaLauncher.Services;

public static class VersionEntryValidator
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Subnautica", "SubnauticaZero", "Subnautica2", "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static bool TryNormalizeGameFolder(string input, out string gameFolder, out string error)
    {
        gameFolder = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Choose a Subnautica folder or Subnautica.exe.";
            return false;
        }

        try
        {
            string candidate = input.Trim().Trim('"');
            if (File.Exists(candidate))
            {
                if (!string.Equals(Path.GetFileName(candidate), "Subnautica.exe", StringComparison.OrdinalIgnoreCase))
                {
                    error = "The selected file must be Subnautica.exe.";
                    return false;
                }

                gameFolder = Path.GetDirectoryName(Path.GetFullPath(candidate))!;
            }
            else
            {
                gameFolder = Path.GetFullPath(candidate);
            }

            if (!File.Exists(Path.Combine(gameFolder, "Subnautica.exe")))
            {
                error = "The selected folder must contain Subnautica.exe.";
                return false;
            }

            return true;
        }
        catch
        {
            error = "That folder path is not valid.";
            return false;
        }
    }

    public static bool IsValidFolderName(string name, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 30)
        {
            error = "Folder Name must be between 1 and 30 characters.";
            return false;
        }

        if (ReservedNames.Contains(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.'))
        {
            error = "Folder Name is reserved or not valid for Windows.";
            return false;
        }

        return true;
    }
}
