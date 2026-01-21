using System.Net.Http;
using System.Text.Json;

namespace SubnauticaLauncher.Updater
{
    public static class UpdateChecker
    {
        private const string Owner = "ItsFrostyYo";
        private const string Repo = "Subnautica-Launcher";

        public static async Task<UpdateInfo?> CheckForUpdateAsync()
        {
            var current = typeof(UpdateChecker).Assembly.GetName().Version!;
            var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SubnauticaLauncher");

            var json = await client.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.GetProperty("tag_name").GetString()!;
            var latest = Version.Parse(tag.TrimStart('v'));

            if (latest <= current)
                return null;

            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString()!;
                if (name.EndsWith(".zip"))
                {
                    return new UpdateInfo
                    {
                        Version = latest,
                        ZipUrl = asset.GetProperty("browser_download_url").GetString()!
                    };
                }
            }

            return null;
        }
    }
}