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
        var appendedPaths = new List<string>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, _) => appendedPaths.Add(path))
            .Returns(Task.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-progress", Kind = JobKind.Export }
        };
        var expectedLogFolder = state.CurrentLogFolder;
        var sink = new PackageProgressSink(state, NullLogger<PackageProgressSink>.Instance);

        sink.Emit(new ProgressEvent { Module = "WorkItems", Stage = "Export", Message = "exported" });
        state.Clear();

        await sink.FlushAsync();

        CollectionAssert.AreEqual(
            new[] { $"{expectedLogFolder}/progress.jsonl" },
            appendedPaths,
            "Buffered progress events must stay in the run-scoped log folder captured while the job was active.");
    }

    [TestMethod]
    public async Task PackageProgressSink_WithActiveStore_AppendsThroughPackageBoundary()
    {
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockPackage = new Mock<IPackage>(MockBehavior.Strict);
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
        var appendedPaths = new List<string>();
        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        mockStore.Setup(s => s.AppendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((path, _, _) => appendedPaths.Add(path))
            .Returns(Task.CompletedTask);

        var state = new ActivePackageState
        {
            CurrentStore = mockStore.Object,
            CurrentJob = new Job { JobId = "job-logger", Kind = JobKind.Import }
        };
        var expectedLogFolder = state.CurrentLogFolder;
        using var provider = new PackageLoggerProvider(
            state,
            Options.Create(new DiagnosticLogOptions { MaxLogFileSizeMB = 50 }));
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("imported");
        state.Clear();

        await provider.FlushAsync();

        CollectionAssert.AreEqual(
            new[] { $"{expectedLogFolder}/agent.jsonl" },
            appendedPaths,
            "Buffered diagnostic logs must stay in the run-scoped log folder captured while the job was active.");
    }
}
#endif
