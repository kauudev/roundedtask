using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows.Forms;

namespace RoundedTask
{
    internal sealed class UpdateCheckResult
    {
        public bool UpdateAvailable;
        public Version CurrentVersion;
        public Version LatestVersion;
        public string LatestTag;
        public string ReleaseName;
        public string ReleaseUrl;
        public string AssetName;
        public string AssetDownloadUrl;
        public long AssetSize;
        public string ErrorMessage;
    }

    internal delegate void UpdateDownloadProgress(int percent, long bytesReceived, long totalBytes);

    internal static class UpdateManager
    {
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/kauudev/roundedtask/releases/latest";
        private const string UserAgent = "RoundedTask-Updater";
        private const int DownloadBufferSize = 81920;

        public static UpdateCheckResult CheckForUpdate()
        {
            UpdateCheckResult result = new UpdateCheckResult();
            result.CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version;

            try
            {
                ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;

                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    client.Headers[HttpRequestHeader.UserAgent] = UserAgent;
                    client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";

                    using (Stream stream = client.OpenRead(LatestReleaseApiUrl))
                    {
                        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(GitHubRelease));
                        GitHubRelease release = serializer.ReadObject(stream) as GitHubRelease;
                        if (release == null || String.IsNullOrEmpty(release.TagName))
                        {
                            result.ErrorMessage = "Nao foi possivel ler a ultima release do GitHub.";
                            return result;
                        }

                        Version latestVersion;
                        if (!TryParseTagVersion(release.TagName, out latestVersion))
                        {
                            result.ErrorMessage = "A tag da ultima release nao parece uma versao valida: " + release.TagName;
                            return result;
                        }

                        GitHubAsset zipAsset = FindZipAsset(release);
                        if (zipAsset == null || String.IsNullOrEmpty(zipAsset.BrowserDownloadUrl))
                        {
                            result.ErrorMessage = "A ultima release nao tem um arquivo .zip publicado.";
                            return result;
                        }

                        result.LatestVersion = latestVersion;
                        result.LatestTag = release.TagName;
                        result.ReleaseName = String.IsNullOrEmpty(release.Name) ? release.TagName : release.Name;
                        result.ReleaseUrl = release.HtmlUrl;
                        result.AssetName = zipAsset.Name;
                        result.AssetDownloadUrl = zipAsset.BrowserDownloadUrl;
                        result.AssetSize = zipAsset.Size;
                        result.UpdateAvailable = latestVersion.CompareTo(NormalizeVersion(result.CurrentVersion)) > 0;
                        return result;
                    }
                }
            }
            catch (WebException ex)
            {
                result.ErrorMessage = "Nao foi possivel conectar ao GitHub: " + ex.Message;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = "Erro ao verificar atualizacao: " + ex.Message;
                return result;
            }
        }

        public static void DownloadAndStartInstaller(UpdateCheckResult update)
        {
            DownloadAndStartInstaller(update, null);
        }

        public static void DownloadAndStartInstaller(UpdateCheckResult update, UpdateDownloadProgress progress)
        {
            if (update == null || String.IsNullOrEmpty(update.AssetDownloadUrl))
            {
                throw new InvalidOperationException("Nao ha atualizacao valida para baixar.");
            }

            string baseDir = Path.Combine(Path.GetTempPath(), "RoundedTaskUpdate", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(baseDir);

            string zipPath = Path.Combine(baseDir, "update.zip");
            string scriptPath = Path.Combine(baseDir, "install-update.ps1");

            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            DownloadFile(update.AssetDownloadUrl, zipPath, progress);

            File.WriteAllText(scriptPath, BuildInstallerScript(), new UTF8Encoding(false));

            Process current = Process.GetCurrentProcess();
            string arguments =
                "-NoProfile -ExecutionPolicy Bypass -File " + Quote(scriptPath) +
                " -ProcessId " + current.Id.ToString() +
                " -ZipPath " + Quote(zipPath) +
                " -InstallDir " + Quote(Application.StartupPath);

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "powershell.exe";
            startInfo.Arguments = arguments;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(startInfo);
        }

        private static void DownloadFile(string url, string zipPath, UpdateDownloadProgress progress)
        {
            HttpWebRequest request = WebRequest.Create(url) as HttpWebRequest;
            if (request == null)
            {
                throw new InvalidOperationException("Nao foi possivel preparar o download.");
            }

            request.UserAgent = UserAgent;
            request.Accept = "application/octet-stream";
            request.AllowAutoRedirect = true;

            using (WebResponse response = request.GetResponse())
            using (Stream source = response.GetResponseStream())
            using (FileStream target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (source == null)
                {
                    throw new InvalidOperationException("O GitHub nao retornou o arquivo da atualizacao.");
                }

                long total = response.ContentLength;
                long received = 0;
                byte[] buffer = new byte[DownloadBufferSize];

                ReportProgress(progress, 0, received, total);

                while (true)
                {
                    int read = source.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        break;
                    }

                    target.Write(buffer, 0, read);
                    received += read;

                    int percent = total > 0 ? (int)Math.Max(0, Math.Min(99, (received * 100L) / total)) : -1;
                    ReportProgress(progress, percent, received, total);
                }

                ReportProgress(progress, 100, received, total);
            }
        }

        private static void ReportProgress(UpdateDownloadProgress progress, int percent, long bytesReceived, long totalBytes)
        {
            if (progress != null)
            {
                progress(percent, bytesReceived, totalBytes);
            }
        }

        private static GitHubAsset FindZipAsset(GitHubRelease release)
        {
            if (release.Assets == null)
            {
                return null;
            }

            GitHubAsset best = null;
            for (int i = 0; i < release.Assets.Length; i++)
            {
                GitHubAsset asset = release.Assets[i];
                if (asset == null || String.IsNullOrEmpty(asset.Name))
                {
                    continue;
                }

                if (!asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (best == null || asset.Size > best.Size)
                {
                    best = asset;
                }
            }

            return best;
        }

        private static bool TryParseTagVersion(string tag, out Version version)
        {
            version = null;
            if (String.IsNullOrEmpty(tag))
            {
                return false;
            }

            string text = tag.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(1);
            }

            Version parsed;
            if (!Version.TryParse(text, out parsed))
            {
                return false;
            }

            version = NormalizeVersion(parsed);
            return true;
        }

        private static Version NormalizeVersion(Version version)
        {
            if (version == null)
            {
                return new Version(0, 0, 0, 0);
            }

            return new Version(
                Math.Max(0, version.Major),
                Math.Max(0, version.Minor),
                Math.Max(0, version.Build),
                Math.Max(0, version.Revision));
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string BuildInstallerScript()
        {
            return
@"param(
    [int]$ProcessId,
    [string]$ZipPath,
    [string]$InstallDir
)

$ErrorActionPreference = 'Stop'
$logPath = Join-Path $InstallDir 'RoundedTask-update.log'

try {
    if ($ProcessId -gt 0) {
        Wait-Process -Id $ProcessId -Timeout 20 -ErrorAction SilentlyContinue
    }

    Start-Sleep -Milliseconds 700

    $extractDir = Join-Path ([System.IO.Path]::GetDirectoryName($ZipPath)) 'extract'
    if (Test-Path -LiteralPath $extractDir) {
        Remove-Item -LiteralPath $extractDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $extractDir | Out-Null
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extractDir -Force

    $newExe = Get-ChildItem -Path $extractDir -Filter 'RoundedTask.exe' -Recurse | Select-Object -First 1
    if (-not $newExe) {
        throw 'RoundedTask.exe nao encontrado dentro do zip da atualizacao.'
    }

    $sourceDir = $newExe.Directory.FullName
    Copy-Item -Path (Join-Path $sourceDir '*') -Destination $InstallDir -Recurse -Force

    Add-Content -LiteralPath $logPath -Value ('Atualizacao instalada em ' + (Get-Date).ToString('s'))
    Start-Process -FilePath (Join-Path $InstallDir 'RoundedTask.exe') -ArgumentList '--tray'
}
catch {
    Add-Content -LiteralPath $logPath -Value ('Falha na atualizacao em ' + (Get-Date).ToString('s') + ': ' + $_.Exception.Message)
    try {
        Start-Process -FilePath (Join-Path $InstallDir 'RoundedTask.exe') -ArgumentList '--tray'
    }
    catch {
    }
}
";
        }

#pragma warning disable 0649
        [DataContract]
        private sealed class GitHubRelease
        {
            [DataMember(Name = "tag_name")]
            public string TagName;

            [DataMember(Name = "name")]
            public string Name;

            [DataMember(Name = "html_url")]
            public string HtmlUrl;

            [DataMember(Name = "assets")]
            public GitHubAsset[] Assets;
        }

        [DataContract]
        private sealed class GitHubAsset
        {
            [DataMember(Name = "name")]
            public string Name;

            [DataMember(Name = "browser_download_url")]
            public string BrowserDownloadUrl;

            [DataMember(Name = "size")]
            public long Size;
        }
#pragma warning restore 0649
    }
}
