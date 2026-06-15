// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Tests for <see cref="WorkItemsOrchestrator.ExportAsync"/> extension-gate behaviour.
/// Drives Stage 3/4: extension enablement controls factory forwarding without reading from the god-object.
/// </summary>
[TestClass]
public sealed class WorkItemsOrchestratorExportTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WhenCommentsDisabled_PassesNullCommentFactoryToExportOrchestrator()
    {
        var spy = new ExportFactorySpy();

        var disabledComments = new CommentsWorkItemExtension(
            Options.Create(new CommentsExtensionOptions { Enabled = false }));

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            exportOrchestratorFactory: spy,
            commentsExtension: disabledComments,
            inlineCommentSourceFactory: Mock.Of<IWorkItemCommentSourceFactory>());

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        Assert.IsNull(spy.CapturedCommentFactory,
            "When CommentsWorkItemExtension.IsEnabled is false, the comment factory must not be forwarded.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WhenAttachmentsDisabled_PassesNullAttachmentSourceToExportOrchestrator()
    {
        var spy = new ExportFactorySpy();

        var disabledAttachments = new AttachmentsWorkItemExtension(
            Options.Create(new AttachmentsExtensionOptions { Enabled = false }),
            NullLogger<AttachmentReplayTool>.Instance);

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            exportOrchestratorFactory: spy,
            attachmentsExtension: disabledAttachments,
            attachmentBinarySource: Mock.Of<IAttachmentBinarySource>());

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        Assert.IsNull(spy.CapturedAttachmentSource,
            "When AttachmentsWorkItemExtension.IsEnabled is false, the attachment source must not be forwarded.");
    }

    private static ExportContext CreateExportContext() => new()
    {
        Job = new Job { JobId = "test-job", Kind = JobKind.Export },
        Package = PackageTestFactory.CreateLooseMock().Object,
        ProgressSink = Mock.Of<IProgressSink>(),
    };

    private sealed class ExportFactorySpy : IWorkItemExportOrchestratorFactory
    {
        public IWorkItemCommentSourceFactory? CapturedCommentFactory { get; private set; }
        public IAttachmentBinarySource? CapturedAttachmentSource { get; private set; }

        public IWorkItemExportOrchestrator Create(
            IPackageAccess package,
            string organisation,
            string project,
            ICheckpointingService checkpointingService,
            IAttachmentBinarySource? attachmentBinarySource,
            IProgressSink? progressSink,
            IWorkItemCommentSourceFactory? inlineCommentSourceFactory,
            IWorkItemFetchService? fetchService,
            IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions,
            IPlatformMetrics? metrics,
            string? jobId,
            string? taskId,
            Microsoft.Extensions.Logging.ILogger? logger,
            string? wiqlQuery,
            IWorkItemDiscoveryService? discoveryService,
            IExportProgressStoreFactory? exportProgressStoreFactory,
            string? packageUri,
            IReferencedPathTracker? referencedPathTracker = null)
        {
            CapturedCommentFactory = inlineCommentSourceFactory;
            CapturedAttachmentSource = attachmentBinarySource;
            var stub = new Mock<IWorkItemExportOrchestrator>(MockBehavior.Loose);
            stub.Setup(o => o.ExportAsync(It.IsAny<IWorkItemRevisionSource>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return stub.Object;
        }
    }
}
