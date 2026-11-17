using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CryoTray.Services
{
    public enum UpdateAssetKind
    {
        Archive,
        Installer,
        Executable,
        Rollback
    }

    public sealed class ReleaseInfo
    {
        public ReleaseInfo(string version, string tagName, string releaseUrl, string assetName,
            string assetDownloadUrl, UpdateAssetKind assetKind, string checksumName,
            string checksumDownloadUrl)
        {
            Version = version;
            TagName = tagName;
            ReleaseUrl = releaseUrl;
            AssetName = assetName;
            AssetDownloadUrl = assetDownloadUrl;
            AssetKind = assetKind;
            ChecksumName = checksumName;
            ChecksumDownloadUrl = checksumDownloadUrl;
        }

        public string Version { get; }
        public string TagName { get; }
        public string ReleaseUrl { get; }
        public string AssetName { get; }
        public string AssetDownloadUrl { get; }
        public UpdateAssetKind AssetKind { get; }
        public string ChecksumName { get; }
        public string ChecksumDownloadUrl { get; }
    }

    public sealed class PreparedUpdate
    {
        public PreparedUpdate(UpdateAssetKind kind, string packagePath, string workDirectory, string version)
        {
            Kind = kind;
            PackagePath = packagePath;
            WorkDirectory = workDirectory;
            Version = version;
        }

        public UpdateAssetKind Kind { get; }
        public string PackagePath { get; }
        public string WorkDirectory { get; }
        public string Version { get; }
    }

    public sealed class UpdateService
    {
        private const string LatestReleaseApi = "https://api.github.com/repos/ZerroDevs/CryoTray/releases/latest";
        private static readonly Regex VersionPattern = new(@"(?<!\d)(\d+)\.(\d+)\.(\d+)(?!\d)", RegexOptions.Compiled);
        private static readonly HttpClient Http = CreateHttpClient();
        private static string? _currentVersion;

        public static UpdateService Instance { get; } = new();

        public static string CurrentVersion => _currentVersion ??= ReadInstalledVersion();

        public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(20));

            using var response = await Http.GetAsync(LatestReleaseApi, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(requestTimeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: requestTimeout.Token).ConfigureAwait(false);
            var root = document.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElement)
                ? tagElement.GetString() ?? string.Empty
                : string.Empty;

            if (!TryParseVersion(tagName, out var version))
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_NoVersion"));
            }

            var asset = SelectAsset(root);
            if (asset == null)
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_NoDownload"));
            }

            var checksum = SelectChecksumAsset(root, asset.Name);
            string releaseUrl = root.TryGetProperty("html_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            return new ReleaseInfo(
                version.ToString(3),
                tagName,
                releaseUrl,
                asset.Name,
                asset.Url,
                asset.Kind,
                checksum?.Name ?? string.Empty,
                checksum?.Url ?? string.Empty);
        }

        public static bool IsNewer(string candidate, string current)
        {
            if (!TryParseVersion(candidate, out var candidateVersion))
            {
                return false;
            }

            if (!TryParseVersion(current, out var currentVersion))
            {
                return true;
            }

            return candidateVersion > currentVersion;
        }

        public async Task<PreparedUpdate> DownloadAsync(ReleaseInfo release, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            string workDirectory = Path.Combine(
                Path.GetTempPath(),
                "CryoTray",
                "Update",
                $"{release.Version}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(workDirectory);

            try
            {
                string fileName = Path.GetFileName(release.AssetName);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = release.AssetKind == UpdateAssetKind.Archive ? "update.zip" : "update.exe";
                }

                string assetPath = Path.Combine(workDirectory, fileName);
                await DownloadFileAsync(release.AssetDownloadUrl, assetPath, progress, cancellationToken).ConfigureAwait(false);
                await VerifyChecksumAsync(release, assetPath, workDirectory, cancellationToken).ConfigureAwait(false);

                if (release.AssetKind != UpdateAssetKind.Archive)
                {
                    AuthenticodeVerifier.VerifyOrAllowUnsigned(assetPath);
                    return new PreparedUpdate(release.AssetKind, assetPath, workDirectory, release.Version);
                }

                string extractDirectory = Path.Combine(workDirectory, "payload");
                Directory.CreateDirectory(extractDirectory);
                ZipFile.ExtractToDirectory(assetPath, extractDirectory, true);

                string? payloadDirectory = FindPayloadDirectory(extractDirectory);
                if (payloadDirectory == null)
                {
                    throw new InvalidOperationException(LocalizationService.Get("Update_NoPayload"));
                }

                AuthenticodeVerifier.VerifyOrAllowUnsigned(Path.Combine(payloadDirectory, "CryoTray.exe"));
                return new PreparedUpdate(UpdateAssetKind.Archive, payloadDirectory, workDirectory, release.Version);
            }
            catch
            {
                TryDeleteDirectory(workDirectory);
                throw;
            }
        }

        public static void LaunchRollback(string backupDirectory)
        {
            if (!Directory.Exists(backupDirectory))
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_BackupMissing"));
            }

            string workDirectory = Path.Combine(
                Path.GetTempPath(),
                "CryoTray",
                "Rollback",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);

            LaunchUpdater(new PreparedUpdate(
                UpdateAssetKind.Rollback,
                backupDirectory,
                workDirectory,
                string.Empty));
        }

        public static void LaunchUpdater(PreparedUpdate update)
        {
            string targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string executablePath = Path.Combine(targetDirectory, "CryoTray.exe");

            if (!File.Exists(executablePath))
            {
                executablePath = Environment.ProcessPath ?? string.Empty;
            }

            if (!File.Exists(executablePath))
            {
                throw new FileNotFoundException(LocalizationService.Get("Update_ExeMissing"));
            }

            string scriptPath = Path.Combine(update.WorkDirectory, "apply-update.ps1");
            File.WriteAllText(scriptPath, UpdaterScript);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = update.WorkDirectory
            };

            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add("-WaitPid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add("-Kind");
            startInfo.ArgumentList.Add(update.Kind.ToString());
            startInfo.ArgumentList.Add("-PackagePath");
            startInfo.ArgumentList.Add(update.PackagePath);
            startInfo.ArgumentList.Add("-TargetDirectory");
            startInfo.ArgumentList.Add(targetDirectory);
            startInfo.ArgumentList.Add("-ExePath");
            startInfo.ArgumentList.Add(executablePath);

            Process.Start(startInfo);
        }

        private static ReleaseChecksum? SelectChecksumAsset(JsonElement root, string assetName)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string expectedName = assetName + ".sha256";
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement) ||
                    !asset.TryGetProperty("browser_download_url", out var urlElement))
                {
                    continue;
                }

                string? name = nameElement.GetString();
                string? url = urlElement.GetString();
                if (string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps)
                {
                    return new ReleaseChecksum(name!, url!);
                }
            }

            return null;
        }

        private static async Task VerifyChecksumAsync(ReleaseInfo release, string assetPath,
            string workDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(release.ChecksumDownloadUrl))
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_ChecksumMissing"));
            }

            string checksumPath = Path.Combine(workDirectory, "checksum.sha256");
            await DownloadFileAsync(release.ChecksumDownloadUrl, checksumPath, null, cancellationToken)
                .ConfigureAwait(false);

            string checksumText = await File.ReadAllTextAsync(checksumPath, cancellationToken)
                .ConfigureAwait(false);
            var match = Regex.Match(checksumText, @"\b[a-fA-F0-9]{64}\b");
            if (!match.Success)
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_ChecksumMalformed"));
            }

            byte[] expected = Convert.FromHexString(match.Value);
            byte[] actual;
            await using (var stream = File.OpenRead(assetPath))
            {
                actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            }

            if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            {
                throw new InvalidOperationException(LocalizationService.Get("Update_ChecksumMismatch"));
            }
        }

        private static ReleaseAsset? SelectAsset(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var candidates = new List<ReleaseAsset>();

            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameElement)
                    || !asset.TryGetProperty("browser_download_url", out var urlElement))
                {
                    continue;
                }

                string? name = nameElement.GetString();
                string? url = urlElement.GetString();

                if (string.IsNullOrWhiteSpace(name)
                    || string.IsNullOrWhiteSpace(url)
                    || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || uri.Scheme != Uri.UriSchemeHttps)
                {
                    continue;
                }

                UpdateAssetKind? kind = null;

                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    kind = UpdateAssetKind.Archive;
                }
                else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    bool isInstaller = name.Contains("setup", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("installer", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("install", StringComparison.OrdinalIgnoreCase);

                    kind = isInstaller ? UpdateAssetKind.Installer : UpdateAssetKind.Executable;
                }

                if (kind.HasValue)
                {
                    candidates.Add(new ReleaseAsset(name, url, kind.Value));
                }
            }

            return candidates.FirstOrDefault(asset => asset.Kind == UpdateAssetKind.Archive)
                ?? candidates.FirstOrDefault(asset => asset.Kind == UpdateAssetKind.Installer)
                ?? candidates.FirstOrDefault(asset => asset.Kind == UpdateAssetKind.Executable);
        }

        private static string? FindPayloadDirectory(string extractDirectory)
        {
            if (File.Exists(Path.Combine(extractDirectory, "CryoTray.exe")))
            {
                return extractDirectory;
            }

            foreach (string directory in Directory.GetDirectories(extractDirectory))
            {
                if (File.Exists(Path.Combine(directory, "CryoTray.exe")))
                {
                    return directory;
                }
            }

            return null;
        }

        private static async Task DownloadFileAsync(string url, string destination, IProgress<int>? progress, CancellationToken cancellationToken)
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            long received = 0;
            int read;

            progress?.Report(0);

            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    progress?.Report((int)Math.Min(100, received * 100 / totalBytes.Value));
                }
            }

            progress?.Report(100);
        }

        private static bool TryParseVersion(string? text, out Version version)
        {
            version = new Version(0, 0, 0);

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = VersionPattern.Match(text);
            if (!match.Success)
            {
                return false;
            }

            if (Version.TryParse(
                $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}",
                out Version? parsed))
            {
                version = parsed;
                return true;
            }

            return false;
        }

        private static string ReadInstalledVersion()
        {
            try
            {
                string versionFile = Path.Combine(AppContext.BaseDirectory, "VERSION");
                if (File.Exists(versionFile) && TryParseVersion(File.ReadAllText(versionFile), out var fileVersion))
                {
                    return fileVersion.ToString(3);
                }
            }
            catch
            {
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (assemblyVersion != null)
            {
                return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{Math.Max(0, assemblyVersion.Build)}";
            }

            return "0.0.0";
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

            client.DefaultRequestHeaders.UserAgent.ParseAdd("CryoTray");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private sealed class ReleaseAsset
        {
            public ReleaseAsset(string name, string url, UpdateAssetKind kind)
            {
                Name = name;
                Url = url;
                Kind = kind;
            }

            public string Name { get; }
            public string Url { get; }
            public UpdateAssetKind Kind { get; }
        }

        private sealed class ReleaseChecksum
        {
            public ReleaseChecksum(string name, string url)
            {
                Name = name;
                Url = url;
            }

            public string Name { get; }
            public string Url { get; }
        }

        private const string UpdaterScript = """
            param(
                [Parameter(Mandatory=$true)][int]$WaitPid,
                [Parameter(Mandatory=$true)][ValidateSet('Archive','Executable','Installer','Rollback')][string]$Kind,
                [Parameter(Mandatory=$true)][string]$PackagePath,
                [Parameter(Mandatory=$true)][string]$TargetDirectory,
                [Parameter(Mandatory=$true)][string]$ExePath
            )

            $ErrorActionPreference = 'Stop'
            $logPath = Join-Path $env:TEMP 'CryoTray-update.log'

            function Write-UpdateLog([string]$message) {
                try {
                    Add-Content -LiteralPath $logPath -Value ("[{0}] {1}" -f (Get-Date -Format o), $message)
                } catch {
                }
            }

            try {
                $deadline = (Get-Date).AddSeconds(45)
                while ((Get-Process -Id $WaitPid -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
                    Start-Sleep -Milliseconds 300
                }

                Start-Sleep -Milliseconds 700

                if ($Kind -eq 'Archive') {
                    Get-ChildItem -LiteralPath $PackagePath -Force | ForEach-Object {
                        Copy-Item -LiteralPath $_.FullName -Destination $TargetDirectory -Recurse -Force
                    }
                } elseif ($Kind -eq 'Rollback') {
                    Get-ChildItem -LiteralPath $TargetDirectory -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
                    Get-ChildItem -LiteralPath $PackagePath -Force | ForEach-Object {
                        Copy-Item -LiteralPath $_.FullName -Destination $TargetDirectory -Recurse -Force
                    }
                } elseif ($Kind -eq 'Executable') {
                    Copy-Item -LiteralPath $PackagePath -Destination $ExePath -Force
                } elseif ($Kind -eq 'Installer') {
                    Start-Process -FilePath $PackagePath
                    Write-UpdateLog 'Installer started.'
                    exit 0
                }

                Start-Process -FilePath $ExePath -WorkingDirectory $TargetDirectory
                Write-UpdateLog 'Update applied and CryoTray restarted.'
            } catch {
                Write-UpdateLog ("Update failed: " + $_.Exception.Message)
                try {
                    if (Test-Path -LiteralPath $ExePath) {
                        Start-Process -FilePath $ExePath -WorkingDirectory $TargetDirectory
                    }
                } catch {
                }
            }
            """;
    }
}
