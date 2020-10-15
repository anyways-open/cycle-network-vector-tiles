using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ANYWAYS.VectorTiles.CycleNetworks
{
    /// <summary>
    /// A class to download and sync files.
    /// </summary>
    public class Downloader
    {
        private readonly ILogger<Downloader> _logger;
        
        /// <summary>
        /// Creates a new downloader.
        /// </summary>
        /// <param name="logger"></param>
        public Downloader(ILogger<Downloader> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// Downloads the given file to the given location and calculates an MD5 for it.
        /// 
        /// If the file already exists on disk, the MD5 will be downloaded first to check if downloading is needed
        /// </summary>
        /// <param name="url">The url to download from.</param>
        /// <param name="local">The full path to where the file will be saved.</param>
        /// <param name="tempPath">A path to use to store the file while downloading. If empty the file will be save directly to the given filename.</param>
        /// <param name="md5Url">The URL where the md5 can be found upstream. If null, {url}.md5 is assumed.</param>
        /// <returns>True if the download succeeded, false otherwise.</returns>
        public async Task<bool> Get(string url, string local,
            string? tempPath = null, string? md5Url = null)
        {
            try
            {
                var localFileInfo = new FileInfo(local);
                var outputPath = localFileInfo.Directory?.FullName;
                if (outputPath == null ||
                    !Directory.Exists(outputPath))
                {
                    _logger.LogError($"Output path not found: {local}");
                    return false;
                }
                
                tempPath ??= outputPath;
                md5Url ??= url + ".md5";
                
                if (!Directory.Exists(tempPath))
                {
                    _logger.LogError($"{nameof(tempPath)} not found: {tempPath}");
                    return false;
                }
                
                using var client = new HttpClient();
                
                // download new md5.
                var localTempMd5 = Path.Combine(tempPath, "." + localFileInfo.Name + ".md5");
                if (!await this.DownloadFileAsync(client, md5Url, localTempMd5))
                {
                    _logger.LogWarning($"MD5 file not found on server at {md5Url}. " +
                                       $"File will always be download completely.");
                }

                // if files exist, first check if they still match.
                var localMd5 = local + ".md5";
                if (File.Exists(local) &&
                    File.Exists(localMd5))
                {
                    // match server and local.
                    if (await MD5Sum.Validate.TryCompareAsync(localMd5, localTempMd5))
                    {
                        File.Delete(localTempMd5);
                        _logger.LogDebug($"Not downloading, hashes match: {url} -> {local}");
                        return false;
                    }
                }

                // download file to local temp path.
                var localTemp = Path.Combine(tempPath, localFileInfo.Name);
                _logger.LogInformation($"Downloading {url}...");
                if (!await this.DownloadFileAsync(client, url, localTemp))
                {
                    _logger.LogError($"Failed to download: {url} -> {local}");
                    return false;
                }

                // move files in place.
                await this.MoveAndOverwrite(localTemp, local);
                await this.MoveAndOverwrite(localTempMd5, localMd5);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to download: {url} -> {local}");
                return false;
            }
        }

        private async Task MoveAndOverwrite(string from, string to)
        {
            if (from == to) return;
            
            await using var fromStream = File.OpenRead(from);
            await using var toStream = File.Open(to, FileMode.Create);
            await fromStream.CopyToAsync(toStream);
        }

        private async Task<bool> DownloadFileAsync(HttpClient client, string url, string local)
        {
            try
            {
                using var client1 = new HttpClient();
                await using var responseStream = await client1.GetStreamAsync(url);
                await using var fileStream = File.Open(local, FileMode.Create);
                await responseStream.CopyToAsync(fileStream);
                return true;
            }
            catch (Exception e)
            {
                _logger.LogError(e, $"Failed downloading: {url} -> {local}");
                return false;
            }
        }
    }
}