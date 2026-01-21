using System.Net.Http;
using System.Text.Json;

public static class UpdateChecker
{
    private const string RepoOwner = "YOUR_GITHUB_USERNAME";
    private const string RepoName = "YOUR_REPO_NAME";

    public static async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var json = await http.GetStringAsync(url);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString(); // v1.0.1
        if (tag == null || !tag.StartsWith("v"))
            return null;

        var version = Version.Parse(tag[1..]);

        if (version <= AppVersion.Current)
            return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            if (name == "SubnauticaLauncher.exe")
            {
                return new UpdateInfo
                {
                    Version = version,
                    DownloadUrl = asset.GetProperty("browser_download_url").GetString()!
                };
            }
        }

        return null;
    }
}

public class UpdateInfo
{
    public Version Version { get; set; } = null!;
    public string DownloadUrl { get; set; } = null!;
}