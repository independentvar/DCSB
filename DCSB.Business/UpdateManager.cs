using Octokit;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace DCSB.Business
{
    public class UpdateManager
    {
        private const string owner = "independentvar";
        private const string repositoryName = "DCSB";
        private const string releasesUrl = "https://github.com/independentvar/DCSB/releases";

        public async Task AutoUpdateCheck(Version currentVersion)
        {
            try
            {
                Release release = await GetNewestRelease();
                Version newVersion = ParseVersion(release);
                if (newVersion > currentVersion)
                {
                    await OfferUpdate(release, newVersion);
                }
            }
            catch { }
        }

        public async Task ManualUpdateCheck(Version currentVersion)
        {
            try
            {
                Release release = await GetNewestRelease();
                Version newVersion = ParseVersion(release);
                if (newVersion > currentVersion)
                {
                    await OfferUpdate(release, newVersion);
                }
                else
                {
                    MessageBox.Show("No update available.");
                }
            }
            catch (Exception ex)
            {
                MessageBoxResult result = MessageBox.Show(
                        $"{ex.Message}\nDo you want to open GitHub to check manually?",
                        "Update check failed",
                        MessageBoxButton.YesNo);
                if (result == MessageBoxResult.Yes)
                {
                    OpenUrl(releasesUrl);
                }
            }
        }

        private async Task OfferUpdate(Release release, Version newVersion)
        {
            MessageBoxResult result = MessageBox.Show(
                        $"Version {newVersion} is available.\nDo you want to download and install it now?",
                        $"New version {newVersion}",
                        MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                string installerPath = await DownloadInstaller(release);

                // the installer cannot replace files of a running instance,
                // so start it and close the application
                Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
            }
            catch (Exception ex)
            {
                MessageBoxResult fallback = MessageBox.Show(
                        $"Downloading the update failed.\n{ex.Message}\n\nDo you want to open {releasesUrl} to download it manually?",
                        "Update failed",
                        MessageBoxButton.YesNo);
                if (fallback == MessageBoxResult.Yes)
                {
                    OpenUrl(releasesUrl);
                }
            }
        }

        // On .NET, Process.Start defaults to UseShellExecute=false, which cannot
        // launch a URL (it treats it as an executable path). Opening a URL in the
        // default browser requires UseShellExecute=true.
        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private async Task<string> DownloadInstaller(Release release)
        {
            ReleaseAsset installerAsset = null;
            foreach (ReleaseAsset asset in release.Assets)
            {
                if (asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerAsset = asset;
                    break;
                }
            }
            if (installerAsset == null)
            {
                throw new InvalidOperationException("The newest release does not contain an installer.");
            }

            string installerPath = Path.Combine(Path.GetTempPath(), installerAsset.Name);
            // Download to a temporary file first, then move it into place, so a
            // failed/partial download never leaves a broken installer behind and a
            // leftover file (possibly locked by antivirus) from a previous attempt
            // does not block the write.
            string downloadPath = installerPath + ".download";
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("independentvar-DCSB");
                using (HttpResponseMessage response = await client.GetAsync(
                    installerAsset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using (Stream downloadStream = await response.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = File.Create(downloadPath))
                    {
                        await downloadStream.CopyToAsync(fileStream);
                    }
                }
            }

            if (File.Exists(installerPath))
            {
                File.Delete(installerPath);
            }
            File.Move(downloadPath, installerPath);
            return installerPath;
        }

        private async Task<Release> GetNewestRelease()
        {
            try
            {
                GitHubClient client = new GitHubClient(new ProductHeaderValue("independentvar-DCSB"));
                var releases = await client.Repository.Release.GetAll(owner, repositoryName);
                return releases[0];
            }
            catch (Exception ex)
            {
                throw new ApiException("Unable to get newest version from GitHub.", ex);
            }
        }

        private Version ParseVersion(Release release)
        {
            return ParseVersion(release.TagName);
        }

        public static Version ParseVersion(string tagName)
        {
            Match match = Regex.Match(tagName, @"\d+\.\d+\.\d+\.\d+");
            if (!match.Success)
            {
                throw new FormatException($"Tag name '{tagName}' does not contain a four-part version number.");
            }
            return Version.Parse(match.Value);
        }
    }
}
