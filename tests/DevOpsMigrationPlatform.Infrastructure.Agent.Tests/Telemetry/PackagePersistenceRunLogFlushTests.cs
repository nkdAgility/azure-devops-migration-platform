// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class PackagePersistenceRunLogFlushTests
{
    [TestMethod]
    public async Task PackageProgressSink_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder()
    {
        var contexts = new List<PackageLogContext>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => contexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
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

    [TestMethod]
    public async Task PackageProgressSink_WithActiveStore_AppendsThroughPackageBoundary()
    {
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Progress),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
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

    [TestMethod]
    public async Task PackageLoggerProvider_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder()
    {
        var contexts = new List<PackageLogContext>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.IsAny<PackageLogContext>(),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageLogContext, PackageLogPayload, CancellationToken>((context, _, _) => contexts.Add(context))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-logger", Kind = JobKind.Import }
        };
        var expectedRunId = state.CurrentRunId;
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 }),
            mockPackage.Object);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("imported");
        state.Clear();

        await provider.FlushAsync();

        Assert.AreEqual(1, contexts.Count, "Expected one persisted diagnostic log batch.");
        Assert.AreEqual(expectedRunId, contexts[0].RunId);
        Assert.AreEqual(PackageLogStream.Diagnostics, contexts[0].Stream);
    }

    [TestMethod]
    public async Task PackageLoggerProvider_WithActiveStore_AppendsThroughPackageBoundary()
    {
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackageAccess>(MockBehavior.Strict);
        mockPackage.Setup(p => p.AppendLogAsync(
                It.Is<PackageLogContext>(c => c.Stream == PackageLogStream.Diagnostics),
                It.IsAny<PackageLogPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-logger", Kind = JobKind.Import }
        };
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 }),
            mockPackage.Object);
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
