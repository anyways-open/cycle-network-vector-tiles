using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Serilog;

namespace ANYWAYS.VectorTiles.CycleNetworks
{

    internal static class Download
    {
        public static async Task Get(IEnumerable<string> urls, string outputPath = null)
        {
            foreach (var url in urls)
            {
                await Get(url, outputPath: outputPath);
            }
        }


        /// <summary>
        /// Downloads the given file to the given location and calculates an MD5 for it.
        /// If the file already exists on disk, the MD5 will be downloaded first to check if downloading is needed
        /// </summary>
        /// <param name="url"></param>
        /// <param name="filename">The name (with directory) where the file will be saved</param>
        /// <param name="outputPath">The directory where `filename` lives, and where `filename.md5` will be saved </param>
        /// <param name="md5Url">The URL where the md5 can be found upstream. If null, will be "url + .md5"</param>
        /// <returns></returns>
        public static async Task Get(string url, string filename = null,
            string outputPath = null, string md5Url = null)
        {
            try
            {
                if (filename == null)
                {
                    var uri = new Uri(url);
                    filename = Path.GetFileName(uri.LocalPath);

                    if (!string.IsNullOrWhiteSpace(outputPath))
                    {
                        filename = Path.Combine(outputPath, filename);
                    }
                }

                if (outputPath == null)
                {
                    outputPath = new FileInfo(filename).DirectoryName;
                }

                if (!Directory.Exists(outputPath))
                {
                    Directory.CreateDirectory(outputPath);
                }

                // if both file and md5 exist, verify they match.
                var md5Filename = Path.Combine(outputPath, filename + ".md5");
                var md5FilenameLatest = Path.Combine(outputPath, filename + ".md5.latest");
                md5Url ??= url + ".md5";
                if (File.Exists(filename))
                {
                    if (File.Exists(md5Filename))
                    {
                        try
                        {
                            using var client = new WebClient();
                            client.DownloadFile(new Uri(md5Url),
                                md5FilenameLatest);
                        }
                        catch (Exception e)
                        {
                            Log.Warning($"MD5 file not found at {md5Url}");
                        }

                        if (File.Exists(md5FilenameLatest))
                        {
                            if (await MD5Sum.Validate.TryCompareAsync(md5Filename, md5FilenameLatest))
                            {
                                File.Delete(md5FilenameLatest);
                                return;
                            }

                            File.Delete(md5Filename);
                            File.Move(md5FilenameLatest, md5Filename);
                        }
                    }
                }

                Log.Information($"Downloading {url}...");
                using var client1 = new WebClient();
                client1.DownloadFile(new Uri(url), filename);

                try
                {
                    using var client = new WebClient();
                    client.DownloadFile(new Uri(md5Url),
                        md5Filename);
                }
                catch (Exception e)
                {
                    Log.Warning($"MD5 file not found at {md5Url}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed downloading: {filename}");
                if (File.Exists(filename)) File.Delete(filename);
            }
        }
    }
}