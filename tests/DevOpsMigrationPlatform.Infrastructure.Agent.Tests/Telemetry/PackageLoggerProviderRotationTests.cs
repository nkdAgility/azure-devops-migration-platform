// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

#if !NETFRAMEWORK
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class PackageLoggerProviderRotationTests
{
    /// <summary>
    /// Verifies that when log output stays under the segment size limit,
    /// all writes go to the initial <c>.migration/Logs/agent.jsonl</c> path.
    /// </summary>
    [TestMethod]
    public async Task FlushBatch_UnderLimit_WritesToInitialSegment()
    {
        // Arrange — 50 MB limit (default), small messages won't rotate.
        var appendedPaths = new List<string>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, _) => appendedPaths.Add(path))
            .Returns(Task.CompletedTask);

        var state = new ActivePackageState { CurrentStore = mockStore.Object };
        var opts = Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 });

        var provider = new PackageLoggerProvider(state, opts);
        var logger = provider.CreateLogger("Test");

        // Act — log 5 messages, then stop.
        using var cts = new CancellationTokenSource();
        var runTask = provider.StartAsync(cts.Token);
        for (int i = 0; i < 5; i++)
            logger.LogInformation("Message {Index}", i);
        await Task.Delay(1500); // let drain loop flush
        await cts.CancelAsync();
        await provider.StopAsync(CancellationToken.None);
        provider.Dispose();

        // Assert — all appends to the initial segment.
        Assert.IsTrue(appendedPaths.Count > 0, "Expected at least one flush.");
        foreach (var path in appendedPaths)
            Assert.AreEqual($"{PackagePaths.Logs}/agent.jsonl", path);
    }

    /// <summary>
    /// Verifies that when cumulative output exceeds the segment limit,
    /// the provider rotates to a new segment path (agent-001.jsonl, etc.).
    /// Uses a 1 MB limit with enough data to trigger rotation.
    /// </summary>
    [TestMethod]
    public async Task FlushBatch_ExceedsLimit_RotatesToNewSegment()
    {
        var appendedPaths = new List<string>();
        long totalBytesAppended = 0;
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, content, _) =>
            {
                appendedPaths.Add(path);
                totalBytesAppended += System.Text.Encoding.UTF8.GetByteCount(content);
            })
            .Returns(Task.CompletedTask);

        var state = new ActivePackageState { CurrentStore = mockStore.Object };
        // 1 MB limit with a large channel and small batches so everything flushes.
        var opts = Options.Create(new DiagnosticLogOptions
        {
            MaxLogFileSizeMB = 1,
            ChannelCapacity = 8192,
            FlushBatchSize = 50,
            FlushIntervalMs = 50
        });

        var provider = new PackageLoggerProvider(state, opts);
        var logger = provider.CreateLogger("Test");

        // Act — log enough large messages to exceed 1 MB.
        using var cts = new CancellationTokenSource();
        var runTask = provider.StartAsync(cts.Token);
        var bigPayload = new string('X', 5_000); // ~5 KB per serialized record
        for (int i = 0; i < 500; i++) // aim for ~2.5 MB total
        {
            logger.LogInformation("{Payload}", bigPayload);
            if (i % 100 == 99)
                await Task.Delay(200); // give drain loop time to flush
        }
        await Task.Delay(3000); // let drain loop flush remaining batches
        await cts.CancelAsync();
        await provider.StopAsync(CancellationToken.None);
        provider.Dispose();

        // Assert — should have at least one write to the rotated segment.
        Assert.IsTrue(appendedPaths.Count > 0, "Expected at least one flush.");
        Assert.IsTrue(totalBytesAppended > 1 * 1024 * 1024,
            $"Expected >1 MB of data to be appended, but got {totalBytesAppended} bytes.");
        Assert.IsTrue(appendedPaths.Exists(p => p == $"{PackagePaths.Logs}/agent.jsonl"),
            "Expected initial segment.");
        Assert.IsTrue(appendedPaths.Exists(p => p == $"{PackagePaths.Logs}/agent-001.jsonl"),
            $"Expected rotation to agent-001.jsonl after exceeding 1 MB. " +
            $"Total bytes: {totalBytesAppended}, flushes: {appendedPaths.Count}.");
    }

    [TestMethod]
    public async Task FlushBatch_RotationDisabled_NeverRotates()
    {
        // Arrange — MaxLogFileSizeMB = 0 disables rotation.
        var appendedPaths = new List<string>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, _) => appendedPaths.Add(path))
            .Returns(Task.CompletedTask);

        var state = new ActivePackageState { CurrentStore = mockStore.Object };
        var opts = Options.Create(new DiagnosticLogOptions
        {
            MaxLogFileSizeMB = 0,
            ChannelCapacity = 8192,
            FlushBatchSize = 50,
            FlushIntervalMs = 50
        });

        var provider = new PackageLoggerProvider(state, opts);
        var logger = provider.CreateLogger("Test");

        // Act — log many large messages.
        using var cts = new CancellationTokenSource();
        var runTask = provider.StartAsync(cts.Token);
        var bigPayload = new string('X', 5_000);
        for (int i = 0; i < 500; i++)
        {
            logger.LogInformation("{Payload}", bigPayload);
            if (i % 100 == 99)
                await Task.Delay(200);
        }
        await Task.Delay(3000);
        await cts.CancelAsync();
        await provider.StopAsync(CancellationToken.None);
        provider.Dispose();

        // Assert — all writes to initial segment only.
        Assert.IsTrue(appendedPaths.Count > 0, "Expected at least one flush.");
        foreach (var path in appendedPaths)
            Assert.AreEqual($"{PackagePaths.Logs}/agent.jsonl", path);
    }

    [TestMethod]
    public void CurrentLogPath_SegmentZero_ReturnsBaseName()
    {
        var state = new ActivePackageState();
        var opts = Options.Create(new DiagnosticLogOptions());
        using var provider = new PackageLoggerProvider(state, opts);

        Assert.AreEqual($"{PackagePaths.Logs}/agent.jsonl", provider.CurrentLogPath);
    }
}
#endif
