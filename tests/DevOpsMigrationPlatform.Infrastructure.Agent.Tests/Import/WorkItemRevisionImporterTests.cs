// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemRevisionImporterTests
{
    [TestMethod]
    public async Task ExecuteAsync_WithAutoResume_DelegatesToRevisionProcessor()
    {
        var fixture = new ImporterFixture();
        var importer = fixture.CreateImporter();

        await importer.ExecuteAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        fixture.Processor.Verify(
            p => p.ProcessAsync(
                ImporterFixture.FolderPath,
                It.IsAny<WorkItemsModuleExtensions>(),
                null,
                fixture.ResolutionStrategy.Object,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithForceFresh_DeletesCursorBeforeImport()
    {
        var fixture = new ImporterFixture();
        var importer = fixture.CreateImporter();

        await importer.ExecuteAsync(new WorkItemsModuleExtensions(), ResumeMode.ForceFresh, CancellationToken.None);

        fixture.Checkpointing.Verify(
            c => c.DeleteCursorAsync("import.workitems", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class ImporterFixture
    {
        public const string FolderPath = "WorkItems/2026-05-17/06387600000000000001-42-0/";

        public Mock<ICheckpointingService> Checkpointing { get; } = new(MockBehavior.Strict);
        public Mock<IProgressSink> ProgressSink { get; } = new(MockBehavior.Loose);
        public Mock<IWorkItemResolutionStrategy> ResolutionStrategy { get; } = new(MockBehavior.Strict);
        public Mock<IIdMapStore> IdMapStore { get; } = new(MockBehavior.Strict);
        public Mock<IWorkItemResolutionProcessor> Processor { get; } = new(MockBehavior.Strict);
        public Mock<IWorkItemTarget> Target { get; } = new(MockBehavior.Strict);
        public Mock<IPackageAccess> Package { get; } = PackageTestFactory.CreateLooseMock();

        public ImporterFixture()
        {
            Checkpointing
                .Setup(c => c.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
            Checkpointing
                .Setup(c => c.DeleteCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            ResolutionStrategy
                .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            IdMapStore
                .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            IdMapStore
                .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<IdMapEntry>());
            IdMapStore
                .Setup(s => s.GetLastRevisionIndexAsync(42, It.IsAny<CancellationToken>()))
                .ReturnsAsync((int?)null);
            IdMapStore
                .Setup(s => s.UpdateLastRevisionIndexAsync(42, 0, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            Package
                .Setup(p => p.EnumerateContentAsync(
                    It.Is<PackageContentContext>(c =>
                        c.IsCollectionRequest &&
                        string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase)),
                    It.IsAny<CancellationToken>()))
                .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(new[] { FolderPath }, ct));

            Processor
                .Setup(p => p.ProcessAsync(
                    FolderPath,
                    It.IsAny<WorkItemsModuleExtensions>(),
                    null,
                    ResolutionStrategy.Object,
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        public WorkItemRevisionImporter CreateImporter()
        {
            var orchestrator = new WorkItemOrchestrator(
                Package.Object,
                "https://dev.azure.com/contoso",
                "Shop",
                Checkpointing.Object,
                ProgressSink.Object,
                ResolutionStrategy.Object,
                IdMapStore.Object,
                Processor.Object,
                Target.Object,
                NullLogger<WorkItemOrchestrator>.Instance);

            return new WorkItemRevisionImporter(orchestrator);
        }

        private static async IAsyncEnumerable<string> ToAsyncEnumerable(
            IEnumerable<string> paths,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var path in paths)
            {
                ct.ThrowIfCancellationRequested();
                yield return path;
                await Task.Yield();
            }
        }
    }
}
