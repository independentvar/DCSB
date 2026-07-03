using Octokit;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
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

        static UpdateManager()
        {
            // GitHub requires TLS 1.2, which .NET 4.5.2 does not enable by default
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        }

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
                    Process.Start(releasesUrl);
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
                Process.Start(installerPath);
                System.Windows.Application.Current.Dispatcher.Invoke(
                    () => System.Windows.Application.Current.Shutdown());
            }
            catch (Exception)
            {
                MessageBoxResult fallback = MessageBox.Show(
                        $"Downloading the update failed.\nDo you want to open {releasesUrl} to download it manually?",
                        "Update failed",
                        MessageBoxButton.YesNo);
                if (fallback == MessageBoxResult.Yes)
                {
                    Process.Start(releasesUrl);
                }
            }
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
            using (WebClient client = new WebClient())
            {
                client.Headers.Add(HttpRequestHeader.UserAgent, "independentvar-DCSB");
                await client.DownloadFileTaskAsync(installerAsset.BrowserDownloadUrl, installerPath);
            }
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
            Match match = Regex.Match(release.TagName, @"\d+\.\d+\.\d+\.\d+");
            return Version.Parse(match.Value);
        }
    }
}
