// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    /// diagnostics are appended through the package boundary.
    /// </summary>
    [TestMethod]
    public async Task FlushBatch_UnderLimit_WritesToInitialSegment()
    {
        // Arrange — 50 MB limit (default), small messages won't rotate.
        var appendedContexts = new List<PackageLogContext>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => appendedContexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-under-limit", Kind = JobKind.Export }
        };
        var opts = Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 });

        var provider = new PackageLoggerProvider(state, opts, mockPackage.Object);
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
        Assert.IsTrue(appendedContexts.Count > 0, "Expected at least one flush.");
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.Stream == PackageLogStream.Diagnostics));
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.RunId == state.CurrentRunId));
    }

    /// <summary>
    /// Verifies that when cumulative output exceeds the segment limit,
    /// diagnostics continue to append through the package boundary.
    /// </summary>
    [TestMethod]
    public async Task FlushBatch_ExceedsLimit_RotatesToNewSegment()
    {
        var appendedContexts = new List<PackageLogContext>();
        long totalBytesAppended = 0;
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, payload, _) =>
            {
                appendedContexts.Add(context);
                using var reader = new System.IO.StreamReader(payload.Content, System.Text.Encoding.UTF8, leaveOpen: true);
                var content = reader.ReadToEnd();
                totalBytesAppended += System.Text.Encoding.UTF8.GetByteCount(content);
            })
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-over-limit", Kind = JobKind.Export }
        };
        // 1 MB limit with a large channel and small batches so everything flushes.
        var opts = Options.Create(new DiagnosticLogOptions
        {
            MaxLogFileSizeMB = 1,
            ChannelCapacity = 8192,
            FlushBatchSize = 50,
            FlushIntervalMs = 50
        });

        var provider = new PackageLoggerProvider(state, opts, mockPackage.Object);
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

        Assert.IsTrue(appendedContexts.Count > 0, "Expected at least one flush.");
        Assert.IsTrue(totalBytesAppended > 1 * 1024 * 1024,
            $"Expected >1 MB of data to be appended, but got {totalBytesAppended} bytes.");
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.Stream == PackageLogStream.Diagnostics));
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.RunId == state.CurrentRunId));
    }

    [TestMethod]
    public async Task FlushBatch_RotationDisabled_NeverRotates()
    {
        // Arrange — MaxLogFileSizeMB = 0 disables rotation.
        var appendedContexts = new List<PackageLogContext>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => appendedContexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-no-rotation", Kind = JobKind.Export }
        };
        var opts = Options.Create(new DiagnosticLogOptions
        {
            MaxLogFileSizeMB = 0,
            ChannelCapacity = 8192,
            FlushBatchSize = 50,
            FlushIntervalMs = 50
        });

        var provider = new PackageLoggerProvider(state, opts, mockPackage.Object);
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
        Assert.IsTrue(appendedContexts.Count > 0, "Expected at least one flush.");
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.Stream == PackageLogStream.Diagnostics));
        Assert.IsTrue(appendedContexts.TrueForAll(c => c.RunId == state.CurrentRunId));
    }

    [TestMethod]
    public void CurrentLogPath_SegmentZero_ReturnsBaseName()
    {
        var state = new ActivePackageState();
        var opts = Options.Create(new DiagnosticLogOptions());
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Loose);
        using var provider = new PackageLoggerProvider(state, opts, mockPackage.Object);

        Assert.AreEqual($"{PackagePathTestHelper.Logs}/diagnostics.ndjson", provider.CurrentLogPath);
    }
}
#endif
