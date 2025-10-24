namespace NatsSandbox;

public sealed class NatsRunnerOptions
{
    private int? _port;
    private int? _monitoringPort;
    private string? _dataDirectory;
    private string? _binaryDirectory;
    private TimeSpan _connectionTimeout = TimeSpan.FromSeconds(30);
    private TimeSpan _dataDirectoryLifetime = TimeSpan.FromHours(12);

    /// <summary>
    /// The port on which the NATS server will listen.
    /// Default is null, which means a random available port will be assigned.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The port must be greater than zero.</exception>
    public int? Port
    {
        get => _port;
        set => _port = value is not <= 0 ? value : throw new ArgumentOutOfRangeException(nameof(Port));
    }

    /// <summary>
    /// The port on which the NATS monitoring HTTP endpoint will listen.
    /// Default is null, which means a random available port will be assigned.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The port must be greater than zero.</exception>
    public int? MonitoringPort
    {
        get => _monitoringPort;
        set => _monitoringPort = value is not <= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MonitoringPort));
    }

    /// <summary>
    /// Enable JetStream for persistence and streaming capabilities.
    /// Default is false.
    /// </summary>
    public bool EnableJetStream { get; set; }

    /// <summary>
    /// Enable debug-level logging from the NATS server.
    /// Default is false.
    /// </summary>
    public bool EnableDebugLogging { get; set; }

    /// <summary>
    /// Enable trace-level logging from the NATS server (very verbose).
    /// Default is false.
    /// </summary>
    public bool EnableTraceLogging { get; set; }

    /// <summary>
    /// A delegate that receives the NATS server's standard output.
    /// Default is null (no logging).
    /// </summary>
    public Action<string>? StandardOutputLogger { get; set; }

    /// <summary>
    /// The directory where the NATS server will store its data.
    /// Default is null, which means a temporary directory will be created and cleaned up on disposal.
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    public string? DataDirectory
    {
        get => _dataDirectory;
        set => _dataDirectory = CheckDirectoryPathFormat(value) is { } ex
            ? throw new ArgumentException(nameof(DataDirectory), ex)
            : value;
    }

    /// <summary>
    /// Provide your own NATS server binary in this directory.
    /// Default is null, which means the library will download it automatically.
    /// </summary>
    /// <exception cref="ArgumentException">The path is invalid.</exception>
    public string? BinaryDirectory
    {
        get => _binaryDirectory;
        set => _binaryDirectory = CheckDirectoryPathFormat(value) is { } ex
            ? throw new ArgumentException(nameof(BinaryDirectory), ex)
            : value;
    }

    /// <summary>
    /// Timeout for the NATS server to start and be ready to accept connections.
    /// Default is 30 seconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The timeout cannot be negative.</exception>
    public TimeSpan ConnectionTimeout
    {
        get => _connectionTimeout;
        set => _connectionTimeout = value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(ConnectionTimeout));
    }

    /// <summary>
    /// Additional arguments to pass to the NATS server.
    /// Default is null.
    /// </summary>
    /// <seealso href="https://docs.nats.io/running-a-nats-service/configuration"/>
    public string? AdditionalArguments { get; set; }

    /// <summary>
    /// The duration for which temporary data directories will be kept.
    /// Ignored when you provide your own data directory.
    /// Default is 12 hours.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The lifetime cannot be negative.</exception>
    public TimeSpan DataDirectoryLifetime
    {
        get => _dataDirectoryLifetime;
        set => _dataDirectoryLifetime = value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(DataDirectoryLifetime));
    }

    /// <summary>
    /// The HTTP transport to use for downloading NATS binaries.
    /// Useful when behind a proxy or firewall.
    /// Default is a new instance using the default HttpClient.
    /// </summary>
    public IHttpTransport Transport { get; set; } = new HttpTransport();

    /// <summary>
    /// The NATS server version to download.
    /// Default is "2.12.1"
    /// </summary>
    public string? Version { get; set; }

    private static Exception? CheckDirectoryPathFormat(string? path)
    {
        if (path == null)
        {
            return null; // null is allowed
        }

        try
        {
            _ = new DirectoryInfo(path);
        }
        catch (Exception ex)
        {
            return ex;
        }

        return null;
    }
}
