# 🧩 NATS Sandbox

> Simple local orchestration of temporary [NATS](https://nats.io) server for .NET 6–9 — built for tests, benchmarks, and experiments.

---

## ✨ Overview

**NATS Sandbox** spins up disposable NATS server with zero manual setup.  
It automatically downloads the NATS binary, starts it locally, and tears it down when finished — so you can focus on messaging, not ops.

**Why you'll love it**

- 🧱 **Self-contained** — no external dependencies; no docker; everything runs in-process  
- ⚡ **Temporary** — perfect for unit & integration testing  
- 🧭 **Cross-platform** — works on Windows, Linux, macOS  
- 🪶 **Simple API** — a single call: `using var runner = NatsRunner.Run();`

---

## 📦 Installation

```bash
dotnet add package NatsSandbox
```

> Requires **.NET 6 or newer**.

---

## Example

```csharp
using NATS.Client.Core;
using NatsSandbox;
using Xunit;

public class IntegrationTests : IAsyncLifetime
{
    private INatsRunner _runner = null!;
    private NatsConnection _nats = null!;

    public async Task InitializeAsync()
    {
        _runner = NatsRunner.Run();
        _nats = new NatsConnection(new NatsOpts { Url = _runner.Url });
        await _nats.ConnectAsync();
    }

    public async Task DisposeAsync()
    {
        await _nats.DisposeAsync();
        _runner.Dispose();
    }

    [Fact]
    public async Task Publish_And_Subscribe_Works()
    {
        var tcs = new TaskCompletionSource<bool>();

        _ = Task.Run(async () =>
        {
            await foreach (var msg in _nats.SubscribeAsync<string>("test.subject"))
            {
                Assert.Equal("Hello", msg.Data);
                tcs.TrySetResult(true);
                break;
            }
        });

        await Task.Delay(200);
        await _nats.PublishAsync("test.subject", "Hello");

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
```
### `NatsRunnerOptions`

| Property | Description | Default |
|-----------|--------------|----------|
| `Port` | TCP port for the NATS server. Leave unset to auto-select a free port. | `null` (auto) |
| `MonitoringPort` | HTTP monitoring port. Leave unset to auto-select a free port. | `null` (auto) |
| `EnableJetStream` | Enable JetStream for persistence and streaming capabilities. | `false` |
| `StandardOutputLogger` | Delegate that receives the NATS server's standard output. | `null` |
| `EnableDebugLogging` | Enable debug-level logging from the NATS server. | `false` |
| `EnableTraceLogging` | Enable trace-level logging from the NATS server (very verbose). | `false` |
| `DataDirectory` | Directory where the NATS server will store its data. Leave unset for a temporary directory that will be cleaned up on disposal. | `null` (auto) |
| `BinaryDirectory` | Provide your own NATS server binary in this directory. Leave unset to download automatically. | `null` (auto) |
| `ConnectionTimeout` | Timeout for the NATS server to start and be ready to accept connections. | 30 seconds |
| `AdditionalArguments` | Additional arguments to pass to the NATS server. | `null` |
| `DataDirectoryLifetime` | Duration for which temporary data directories will be kept. Ignored when you provide your own data directory. | 12 hours |
| `Transport` | HTTP transport to use for downloading NATS binaries. Useful when behind a proxy or firewall. | Default `HttpClient` transport |
| `Version` | NATS server version to download. | 2.12.1 |

You can fully customise HTTP behavior (headers, auth token, rate-limit handling) via your own `IHttpTransport` implementation.

#### Use a Specific Port

```csharp
var runner = NatsRunner.Run(new NatsRunnerOptions
{
    Port = 4222
});
```

#### Enable Logging

```csharp
var runner = NatsRunner.Run(new NatsRunnerOptions
{
    StandardOutputLogger = Console.WriteLine,
    EnableDebugLogging = true
});
```

### Troubleshooting

#### Test Hangs or Times Out

The NATS binary download might be slow on first run. Subsequent runs use the cached binary.

#### Port Already in Use

Don't specify a port - let NatsSandbox choose a random available port (default behavior).

#### Windows Firewall Prompt

Click "Allow" when prompted. NATS needs to open a network port for communication.

---

## 🖥️ Supported Platforms

| OS | Architecture | Supported |
|----|---------------|-------|
| Windows | x64 / arm64 | ✅ |
| Linux | x64, arm64, arm | ✅ |
| macOS | x64, arm64 | ✅ |

---

## 🧰 Development Notes

- Caches binaries per version tag in `%LOCALAPPDATA%/nats-sandbox/bin/vX.Y.Z`
- Performs platform-specific extraction and sets executable permissions on Unix
- Thread-safe via internal `SemaphoreSlim` during version checks

---

## 🪪 License

MIT © Wassim K— see [`LICENSE`](./LICENSE)
