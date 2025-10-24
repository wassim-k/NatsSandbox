namespace NatsSandbox;

public interface INatsRunner : IDisposable
{
    /// <summary>
    /// The NATS connection string (e.g., "nats://localhost:4222").
    /// </summary>
    string Url { get; }

    /// <summary>
    /// The port on which the NATS server is listening.
    /// </summary>
    int Port { get; }

    /// <summary>
    /// The port on which the NATS monitoring HTTP endpoint is listening.
    /// </summary>
    int MonitoringPort { get; }

    /// <summary>
    /// The directory where the NATS server stores its data.
    /// </summary>
    string DataDirectory { get; }
}
