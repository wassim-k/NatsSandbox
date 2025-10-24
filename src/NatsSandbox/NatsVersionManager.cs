using System.Runtime.InteropServices;

namespace NatsSandbox;

internal static class NatsVersionManager
{
    private const string BinaryNameWithoutExt = "nats-server";
    private const string DefaultNatsVersion = "2.12.1";

    private static readonly string BinaryFileName =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{BinaryNameWithoutExt}.exe"
            : BinaryNameWithoutExt;

    private static readonly SemaphoreSlim DownloadMutex = new(1, 1);

    public static async Task<string> EnsureNatsBinaryAsync(
        NatsRunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        // 1) honor custom binary folder immediately
        if (!string.IsNullOrWhiteSpace(options.BinaryDirectory))
        {
            var customPath = Path.Combine(options.BinaryDirectory, BinaryFileName);

            if (File.Exists(customPath))
            {
                return customPath;
            }

            throw new FileNotFoundException(
                $"The provided binary directory '{options.BinaryDirectory}' does not contain the executable '{BinaryFileName}'.",
                customPath);
        }

        // 2) Determine version and check cache
        var version = $"v{options.Version ?? DefaultNatsVersion}";

        var baseExeDirPath = Path.Combine(FileManager.GetAppDataDir(), "bin");
        var versionDir = Path.Combine(baseExeDirPath, version);
        var binaryPath = Path.Combine(versionDir, BinaryFileName);

        if (File.Exists(binaryPath))
        {
            return binaryPath;
        }

        // 3) Download if not in cache
        await DownloadMutex.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Double-check after acquiring lock
            if (File.Exists(binaryPath))
            {
                return binaryPath;
            }

            // 4) download + extract via FileManager
            var assetUrl = GetDownloadUrlForPlatform(version);
            await DownloadAndExtractAsync(options.Transport, assetUrl, versionDir, binaryPath, cancellationToken).ConfigureAwait(false);

            return binaryPath;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NatsSandboxException($"NATS server version '{version}' was not found.", ex);
        }
        finally
        {
            DownloadMutex.Release();
        }
    }

    private static string GetDownloadUrlForPlatform(string version)
    {
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "amd64"
        };

        var platform =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" :
            throw new PlatformNotSupportedException("Current operating system is not supported");

        var extension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".zip" : ".tar.gz";

        var expectedName = $"nats-server-{version}-{platform}-{arch}{extension}";

        const string baseUrl = "https://github.com/nats-io/nats-server/releases/download";

        return $"{baseUrl}/{version}/{expectedName}";
    }

    private static async Task DownloadAndExtractAsync(
        IHttpTransport transport,
        string downloadUrl,
        string versionDirPath,
        string binaryPath,
        CancellationToken cancellationToken)
    {
        using var work = FileManager.CreateTempWorkDir();
        var archivePath = Path.Combine(work.DownloadDir, "nats-server-archive");

        // Download
        using (var downloadStream = await transport.DownloadAsync(downloadUrl, cancellationToken).ConfigureAwait(false))
        {
            await FileManager.SaveStreamAsync(downloadStream, archivePath, cancellationToken).ConfigureAwait(false);
        }

        // Extract + locate binary
        FileManager.ExtractArchive(archivePath, work.ExtractDir);

        var extractedBinary = Path.Join(work.ExtractDir, Path.GetFileName(binaryPath));

        if (!File.Exists(extractedBinary))
        {
            throw new NatsSandboxException($"Could not find {Path.GetFileName(binaryPath)} in downloaded archive");
        }

        // Copy to final location
        FileManager.EnsureDirectory(versionDirPath);
        FileManager.CopyFile(extractedBinary, binaryPath, overwrite: true);
        FileManager.MakeExecutableIfUnix(binaryPath);
    }
}