using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace SubnauticaLauncher.Updater
{
    public static class UpdateDownloader
    {
        public static async Task<string> DownloadAndExtractAsync(string zipUrl)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "SNLUpdate_" + Guid.NewGuid());
            Directory.CreateDirectory(tempRoot);

            var zipPath = Path.Combine(tempRoot, "update.zip");

            using var client = new HttpClient();
            await using (var fs = File.Create(zipPath))
            {
                var stream = await client.GetStreamAsync(zipUrl);
                await stream.CopyToAsync(fs);
            }

            ZipFile.ExtractToDirectory(zipPath, tempRoot, true);

            return tempRoot;
        }
    }
}