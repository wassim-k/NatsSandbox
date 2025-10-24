using System.Runtime.InteropServices;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace NatsSandbox;

public static class FileManager
{
    public static string GetAppDataDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "nats-sandbox");

    public static string GetTempRoot()
        => Path.Combine(Path.GetTempPath(), "nats-sandbox");

    public static TempWorkDir CreateTempWorkDir(string prefix = "nats-")
    {
        var root = GetTempRoot();
        var name = $"{prefix}{Path.GetRandomFileName()}";
        var download = Path.Combine(root, "downloads", name);
        var extract = Path.Combine(root, "extract", name);
        Directory.CreateDirectory(download);
        Directory.CreateDirectory(extract);
        return new TempWorkDir(download, extract);
    }

    public static async Task SaveStreamAsync(Stream src, string destFilePath, CancellationToken ct)
    {
        EnsureDirectory(Path.GetDirectoryName(destFilePath)!);
        await using var fs = File.Create(destFilePath);
        await src.CopyToAsync(fs, ct);
        await fs.FlushAsync(ct);
    }

    public static void ExtractArchive(string archivePath, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        using var stream = File.OpenRead(archivePath);
        using var reader = ReaderFactory.Open(stream);
        
        while (reader.MoveToNextEntry())
        {
            if (!reader.Entry.IsDirectory)
            {
                reader.WriteEntryToDirectory(destinationDir, new ExtractionOptions
                {
                    ExtractFullPath = false,
                    Overwrite = true
                });
            }
        }
    }

    public static void EnsureDirectory(string path)
        => Directory.CreateDirectory(path);

    public static void CopyFile(string source, string destination, bool overwrite = true)
    {
        EnsureDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite);
    }

    public static void MakeExecutableIfUnix(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "chmod",
            Arguments = $"+x \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        p?.WaitForExit();
    }

    public static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch { /* swallow */ }
    }
}

public sealed class TempWorkDir : IDisposable
{
    public string DownloadDir { get; }
    public string ExtractDir { get; }

    internal TempWorkDir(string downloadDir, string extractDir)
    {
        DownloadDir = downloadDir;
        ExtractDir = extractDir;
    }

    public void Dispose()
    {
        FileManager.TryDeleteDirectory(DownloadDir);
        FileManager.TryDeleteDirectory(ExtractDir);
    }
}
