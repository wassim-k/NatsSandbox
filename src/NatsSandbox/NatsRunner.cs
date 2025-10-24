using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using NATS.Client.Core;

namespace NatsSandbox;

public sealed class NatsRunner : INatsRunner
{
    private readonly Process _process;
    private readonly string _dataDirectory;
    private readonly bool _shouldDeleteDataDirectory;
    private bool _disposed;

    public string Url { get; }

    public int Port { get; }

    public int MonitoringPort { get; }

    public string DataDirectory => _dataDirectory;

    private NatsRunner(NatsRunnerOptions options)
    {
        Port = options.Port ?? GetAvailablePort();
        MonitoringPort = options.MonitoringPort ?? GetAvailablePort();

        _dataDirectory = !string.IsNullOrWhiteSpace(options.DataDirectory)
            ? options.DataDirectory
            : Path.Combine(Path.GetTempPath(), $"nats-sandbox-{Guid.NewGuid():N}");

        _shouldDeleteDataDirectory = string.IsNullOrWhiteSpace(options.DataDirectory);

        Url = $"nats://localhost:{Port}";

        // Cleanup old directories
        if (_shouldDeleteDataDirectory)
        {
            CleanupOldDataDirectories(options.DataDirectoryLifetime);
        }

        // Ensure data directory exists
        Directory.CreateDirectory(_dataDirectory);

        var binaryPath = NatsVersionManager.EnsureNatsBinaryAsync(options).GetAwaiter().GetResult();

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binaryPath,
                Arguments = BuildArguments(options, Port, MonitoringPort, _dataDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        try
        {
            if (options.StandardOutputLogger != null)
            {
                _process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        options.StandardOutputLogger(e.Data);
                    }
                };

                _process.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        options.StandardOutputLogger(e.Data);
                    }
                };
            }

            NativeMethods.EnsureProcessesAreKilledWhenCurrentProcessIsKilled();

            StartProcessWithRetryAsync(_process).GetAwaiter().GetResult();

            if (options.StandardOutputLogger != null)
            {
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
            }

            WaitForNatsReadyAsync(Url, options.ConnectionTimeout, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public static INatsRunner Run(NatsRunnerOptions? options = null)
    {
        return new NatsRunner(options ?? new NatsRunnerOptions());
    }

    private static string BuildArguments(NatsRunnerOptions options, int port, int monitoringPort, string dataDir)
    {
        var args = new List<string>
        {
            $"--port {port}",
            $"--http_port {monitoringPort}"
        };

        if (options.EnableJetStream)
        {
            var storeDir = Path.Combine(dataDir, "jetstream");
            args.Add("--jetstream");
            args.Add($"--store_dir \"{storeDir}\"");
        }

        if (options.EnableDebugLogging)
        {
            args.Add("--debug");
        }

        if (options.EnableTraceLogging)
        {
            args.Add("--trace");
        }

        if (!string.IsNullOrWhiteSpace(options.AdditionalArguments))
        {
            args.Add(options.AdditionalArguments);
        }

        return string.Join(" ", args);
    }

    private static async Task StartProcessWithRetryAsync(Process process)
    {
        const int maxAttempts = 3;
        const int retryDelayMs = 50;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                process.Start();
                return;
            }
            // This exception rarely happens on Linux in CI during tests with high concurrency
            // System.ComponentModel.Win32Exception : An error occurred trying to start process with working directory. Text file busy
            catch (Win32Exception ex) when (ex.Message.IndexOf("text file busy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (attempt == maxAttempts)
                {
                    throw;
                }

                await Task.Delay(retryDelayMs).ConfigureAwait(false);
            }
        }
    }

    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private async Task WaitForNatsReadyAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var isReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnProcessExited(object? sender, EventArgs e)
        {
            isReadyTcs.TrySetResult(false);
        }

        _process.Exited += OnProcessExited;

        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
            using (combinedCts.Token.Register(() => isReadyTcs.TrySetCanceled(combinedCts.Token)))
            {
                // Poll for readiness
                _ = Task.Run(async () =>
                {
                    while (!combinedCts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            await using var nats = new NatsConnection(new NatsOpts { Url = url });
                            await nats.ConnectAsync().ConfigureAwait(false);
                            isReadyTcs.TrySetResult(true);
                            return;
                        }
                        catch
                        {
                            await Task.Delay(100, combinedCts.Token).ConfigureAwait(false);
                        }
                    }
                }, combinedCts.Token);

                try
                {
                    await isReadyTcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                {
                    var timeoutMessage = string.Format(
                        CultureInfo.InvariantCulture,
                        "NATS server did not become ready within the specified timeout of {0} seconds. Consider increasing the value of '{1}'.",
                        timeout.TotalSeconds,
                        nameof(NatsRunnerOptions.ConnectionTimeout));

                    throw new TimeoutException(timeoutMessage);
                }

                HandleUnexpectedProcessExit();
            }
        }
        finally
        {
            _process.Exited -= OnProcessExited;
        }
    }

    private void HandleUnexpectedProcessExit()
    {
        if (_process.HasExited)
        {
            // WaitForExit ensures that all output is flushed before we throw the exception,
            // ensuring we asynchronously capture all standard output and error messages.
            _process.WaitForExit();
            throw CreateExceptionFromUnexpectedProcessExit();
        }
    }

    private NatsSandboxException CreateExceptionFromUnexpectedProcessExit()
    {
        var exitCode = _process.ExitCode;
        var processDescription = $"{_process.StartInfo.FileName} {_process.StartInfo.Arguments}";

        return new NatsSandboxException($"The NATS server process '{processDescription}' exited unexpectedly with code {exitCode}.");
    }

    private static void CleanupOldDataDirectories(TimeSpan lifetime)
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var directories = Directory.GetDirectories(tempPath, "nats-sandbox-*");
            var cutoffTime = DateTime.UtcNow - lifetime;

            foreach (var dir in directories)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTimeUtc < cutoffTime)
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch
                {
                    // Ignore errors when cleaning up old directories
                }
            }
        }
        catch
        {
            // Ignore errors in cleanup
        }
    }

    private void Cleanup()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        catch
        {
            // Ignore errors when killing process
        }

        try
        {
            _process.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }

        if (_shouldDeleteDataDirectory && Directory.Exists(_dataDirectory))
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch
            {
                // Ignore errors when deleting data directory
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Cleanup();
        _disposed = true;
    }
}
