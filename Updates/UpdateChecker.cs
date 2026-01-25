using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.IO;
using SubnauticaLauncher.UI;
using SubnauticaLauncher.Versions;
using SubnauticaLauncher.Updates;
using SubnauticaLauncher.Installer;

namespace SubnauticaLauncher.Updates;

public static class UpdateChecker
{
    private const string Owner = "ItsFrostyYo";
    private const string Repo = "Subnautica-Launcher";

    private static readonly HttpClient http = new();

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        var current = Assembly.GetExecutingAssembly().GetName().Version!;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

        var json = await http.GetStringAsync(
            $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString()!.TrimStart('v');
        var latest = new Version(tag);

        if (latest <= current)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == "SubnauticaLauncher.exe")
            {
                return new UpdateInfo
                {
                    Version = latest,
                    DownloadUrl = asset.GetProperty("browser_download_url").GetString()!
                };
            }
        }

        return null;
    }
   }