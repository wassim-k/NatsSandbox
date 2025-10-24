using AwesomeAssertions;
using NATS.Client.Core;
using NATS.Client.JetStream.Models;
using NATS.Net;
using Xunit;

namespace NatsSandbox.Tests;

public class NatsRunnerTests
{
    [Fact]
    public void Run_WithDefaultOptions_ShouldStartNatsServer()
    {
        // Arrange & Act
        using var runner = NatsRunner.Run();

        // Assert
        runner.Url.Should().NotBeNullOrEmpty();
        runner.Url.Should().StartWith("nats://localhost:");
        runner.Port.Should().BeGreaterThan(0);
        runner.MonitoringPort.Should().BeGreaterThan(0);
        runner.DataDirectory.Should().NotBeNullOrEmpty();
        Directory.Exists(runner.DataDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task Run_ShouldAllowBasicPublishSubscribe()
    {
        // Arrange
        using var runner = NatsRunner.Run();
        await using var nats = new NatsConnection(new NatsOpts { Url = runner.Url });
        await nats.ConnectAsync();

        var receivedMessage = string.Empty;
        var tcs = new TaskCompletionSource<bool>();

        // Act
        _ = Task.Run(async () =>
        {
            await foreach (var msg in nats.SubscribeAsync<string>("test.subject"))
            {
                receivedMessage = msg.Data!;
                tcs.SetResult(true);
                break;
            }
        });

        await Task.Delay(200); // Give subscription time to register
        await nats.PublishAsync("test.subject", "Hello NATS!");

        // Assert
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        receivedMessage.Should().Be("Hello NATS!");
    }

    [Fact]
    public async Task Run_WithJetStreamEnabled_ShouldPublishMessage()
    {
        // Arrange
        var options = new NatsRunnerOptions
        {
            EnableJetStream = true
        };

        using var runner = NatsRunner.Run(options);
        await using var nats = new NatsConnection(new NatsOpts { Url = runner.Url });
        await nats.ConnectAsync();

        var js = nats.CreateJetStreamContext();

        // Create a stream
        await js.CreateStreamAsync(new StreamConfig
        {
            Name = "TEST_STREAM",
            Subjects = ["test.>"]
        });

        // Act
        var ack = await js.PublishAsync("test.subject", "Hello JetStream!");

        // Assert
        ack.Should().NotBeNull();
        ack.Stream.Should().Be("TEST_STREAM");
        ack.Seq.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Run_WithCustomPort_ShouldUseSpecifiedPort()
    {
        // Arrange
        var customPort = 14222;
        var options = new NatsRunnerOptions
        {
            Port = customPort
        };

        // Act
        using var runner = NatsRunner.Run(options);

        // Assert
        runner.Port.Should().Be(customPort);
        runner.Url.Should().Contain($":{customPort}");
    }

    [Fact]
    public void Run_WithCustomDataDirectory_ShouldUseSpecifiedDirectory()
    {
        // Arrange
        var customDir = Path.Combine(Path.GetTempPath(), $"nats-test-{Guid.NewGuid():N}");
        var options = new NatsRunnerOptions
        {
            DataDirectory = customDir
        };

        try
        {
            // Act
            using var runner = NatsRunner.Run(options);

            // Assert
            runner.DataDirectory.Should().Be(customDir);
            Directory.Exists(customDir).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(customDir))
            {
                Directory.Delete(customDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Run_WithLogging_ShouldCaptureOutput()
    {
        // Arrange
        var outputLines = new List<string>();
        var errorLines = new List<string>();

        var options = new NatsRunnerOptions
        {
            StandardOutputLogger = outputLines.Add,
            EnableDebugLogging = true
        };

        // Act
        using var runner = NatsRunner.Run(options);
        await Task.Delay(200); // Give time for some log output

        // Assert
        outputLines.Should().NotBeEmpty();
    }

    [Fact]
    public void Dispose_ShouldCleanupProcess()
    {
        // Arrange
        var runner = NatsRunner.Run();
        var url = runner.Url;

        // Act
        runner.Dispose();

        // Assert - attempting to connect should fail
        var act = async () =>
        {
            await using var nats = new NatsConnection(new NatsOpts
            {
                Url = url,
                ConnectTimeout = TimeSpan.FromSeconds(2)
            });
            await nats.ConnectAsync();
        };

        act.Should().ThrowAsync<Exception>();
    }

    [Fact]
    public void Dispose_WithTemporaryDirectory_ShouldDeleteDataDirectory()
    {
        // Arrange
        string? dataDirectory;

        using (var runner = NatsRunner.Run())
        {
            dataDirectory = runner.DataDirectory;
            Directory.Exists(dataDirectory).Should().BeTrue();
        }

        // Act & Assert
        // Give the cleanup some time
        Thread.Sleep(1000);
        Directory.Exists(dataDirectory).Should().BeFalse();
    }

    [Fact]
    public void Dispose_WithCustomDirectory_ShouldNotDeleteDataDirectory()
    {
        // Arrange
        var customDir = Path.Combine(Path.GetTempPath(), $"nats-test-{Guid.NewGuid():N}");
        var options = new NatsRunnerOptions
        {
            DataDirectory = customDir
        };

        try
        {
            using (var runner = NatsRunner.Run(options))
            {
                Directory.Exists(customDir).Should().BeTrue();
            }

            // Act & Assert
            Directory.Exists(customDir).Should().BeTrue();
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(customDir))
            {
                Directory.Delete(customDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Run_MultipleInstances_ShouldNotConflict()
    {
        // Arrange & Act
        using var runner1 = NatsRunner.Run();
        using var runner2 = NatsRunner.Run();

        // Assert
        runner1.Port.Should().NotBe(runner2.Port);
        runner1.MonitoringPort.Should().NotBe(runner2.MonitoringPort);
        runner1.Url.Should().NotBe(runner2.Url);

        // Verify both are functional
        await using var nats1 = new NatsConnection(new NatsOpts { Url = runner1.Url });
        await using var nats2 = new NatsConnection(new NatsOpts { Url = runner2.Url });

        await nats1.ConnectAsync();
        await nats2.ConnectAsync();

        await nats1.PublishAsync("test", "data");
        await nats2.PublishAsync("test", "data");
    }

    [Fact]
    public void Run_WithAnInvalidVersion_ShouldThrowAnException()
    {
        // Arrange & Act
        var exception = Assert.Throws<NatsSandboxException>(() => NatsRunner.Run(new NatsRunnerOptions { Version = "0.0.999" }));

        // Assert
        exception.Message.Should().Contain("NATS server version 'v0.0.999' was not found.");
    }
}
