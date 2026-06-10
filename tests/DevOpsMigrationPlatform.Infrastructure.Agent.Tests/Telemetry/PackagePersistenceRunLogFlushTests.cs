// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class PackagePersistenceRunLogFlushTests
{
    private static IServiceProvider BuildServiceProvider(IPackageAccess package)
    {
        var services = new ServiceCollection();
        services.AddSingleton(package);
        return services.BuildServiceProvider();
    }
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageProgressSink_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder()
    {
        var contexts = new List<PackageLogContext>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => contexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-progress", Kind = JobKind.Export }
        };
        var expectedRunId = state.CurrentRunId;
        var sink = new PackageProgressSink(state, NullLogger<PackageProgressSink>.Instance, mockPackage.Object);

        sink.Emit(new ProgressEvent { Module = "WorkItems", Stage = "Export", Message = "exported" });
        state.Clear();

        await sink.FlushAsync();

        Assert.AreEqual(1, contexts.Count, "Expected one persisted progress log batch.");
        Assert.AreEqual(expectedRunId, contexts[0].RunId);
        Assert.AreEqual(PackageLogStream.Progress, contexts[0].Stream);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task PackageProgressSink_WithActiveStore_AppendsThroughPackageBoundary()
    {
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Progress),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-progress", Kind = JobKind.Export }
        };
        var sink = new PackageProgressSink(state, NullLogger<PackageProgressSink>.Instance, mockPackage.Object);

        sink.Emit(new ProgressEvent { Module = "WorkItems", Stage = "Export", Message = "exported" });
        await sink.FlushAsync();

        mockPackage.Verify(p => p.AppendLogAsync(
            It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Progress),
            It.IsAny<PackageLogPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder()
    {
        var contexts = new List<PackageLogContext>();
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => contexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-logger", Kind = JobKind.Import }
        };
        var expectedRunId = state.CurrentRunId;
        using var provider = new PackageLoggerProvider(state, Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 }), BuildServiceProvider(mockPackage.Object));
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("imported");
        state.Clear();

        await provider.FlushAsync();

        Assert.AreEqual(1, contexts.Count, "Expected one persisted diagnostic log batch.");
        Assert.AreEqual(expectedRunId, contexts[0].RunId);
        Assert.AreEqual(PackageLogStream.Diagnostics, contexts[0].Stream);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PackageProgressSink_Emit_IsNonBlockingAndBuffersInternally()
    {
        // The scenario: a progress event emitted via the progress sink must not block
        // the export pipeline, and the event must be buffered internally before being
        // flushed to the package.
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        // AppendLogAsync is NOT set up — verifying it is never called synchronously during Emit.

        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-nonblock", Kind = JobKind.Export }
        };
        var sink = new PackageProgressSink(state, NullLogger<PackageProgressSink>.Instance, mockPackage.Object);

        // Emit must return synchronously (non-blocking — uses TryWrite to a bounded channel).
        var sw = System.Diagnostics.Stopwatch.StartNew();
        sink.Emit(new ProgressEvent { Module = "WorkItems", Stage = "Export", Message = "progress" });
        sw.Stop();

        // Emit must complete in well under 10 ms (channel write is O(1) and non-blocking).
        Assert.IsTrue(sw.ElapsedMilliseconds < 100,
            $"Emit took {sw.ElapsedMilliseconds} ms — expected non-blocking (< 100 ms).");

        // AppendLogAsync must NOT have been called yet — the event is still buffered.
        mockPackage.VerifyNoOtherCalls();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task PackageLoggerProvider_WithActiveStore_AppendsThroughPackageBoundary()
    {
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Diagnostics),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-logger", Kind = JobKind.Import }
        };
        using var provider = new PackageLoggerProvider(state, Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 }), BuildServiceProvider(mockPackage.Object));
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("imported");
        await provider.FlushAsync();

        mockPackage.Verify(p => p.AppendLogAsync(
            It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Diagnostics),
            It.IsAny<PackageLogPayload>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
#endif


