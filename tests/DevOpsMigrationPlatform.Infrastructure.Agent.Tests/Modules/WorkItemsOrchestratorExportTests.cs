// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
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
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Tests for <see cref="WorkItemsOrchestrator.ExportAsync"/> extension-gate behaviour.
/// </summary>
[TestClass]
public sealed class WorkItemsOrchestratorExportTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WhenCommentsDisabled_CommentsExtensionIsNotEnabled()
    {
        var spy = new ExportFactorySpy();

        var disabledComments = new CommentsWorkItemExtension(
            Options.Create(new CommentsExtensionOptions { Enabled = false }),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All);

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            exportOrchestratorFactory: spy,
            commentsExtension: disabledComments);

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        // Verify the extension passed to the export orchestrator has IsEnabled = false.
        var capturedExt = spy.CapturedExtensions?.OfType<CommentsWorkItemExtension>().FirstOrDefault();
        Assert.IsNotNull(capturedExt, "CommentsWorkItemExtension should be in the export extensions list.");
        Assert.IsFalse(capturedExt.IsEnabled,
            "When CommentsExtensionOptions.Enabled is false, IsEnabled must be false.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_WhenAttachmentBinarySourceProvided_ForwardsSourceToExportOrchestrator()
    {
        // Attachments are always-on core behaviour — the binary source is always forwarded if available.
        var spy = new ExportFactorySpy();
        var binarySource = Mock.Of<IAttachmentBinarySource>();

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            exportOrchestratorFactory: spy,
            attachmentBinarySource: binarySource);

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        Assert.AreEqual(binarySource, spy.CapturedAttachmentSource,
            "Attachment binary source should always be forwarded to the export orchestrator.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_ConfiguredQueryWithoutFilters_ForwardsTheFetchServiceAndQuery()
    {
        // #171 live E2E: the fetch service was forwarded only when filters were set, so a
        // configured WIQL on its own never bounded the export.
        const string query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] <> 'Issue'";
        var spy = new ExportFactorySpy();
        var fetchService = Mock.Of<IWorkItemFetchService>();
        var options = Options.Create(new WorkItemsModuleOptions { Selection = new WorkItemsSelectionOptions { Query = query } });

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            exportOrchestratorFactory: spy, fetchService: fetchService, options: options);

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        Assert.AreSame(fetchService, spy.CapturedFetchService);
        Assert.AreEqual(query, spy.CapturedQuery);
    }

    // PR #222 review (stream, never materialise): the query bounds the export at the revision
    // source, which streams it, instead of a pre-pass that holds every matching ID in memory.
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ExportAsync_ConfiguredQuery_IsGivenToTheRevisionSource()
    {
        const string query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project AND [System.WorkItemType] <> 'Issue'";
        var sourceFactory = new Mock<IWorkItemRevisionSourceFactory>();
        sourceFactory.Setup(f => f.CreateAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<IWorkItemRevisionSource>());
        var options = Options.Create(new WorkItemsModuleOptions { Selection = new WorkItemsSelectionOptions { Query = query } });

        var orchestrator = WorkItemsModuleTestFactory.CreateOrchestrator(
            sourceFactory: sourceFactory.Object, exportOrchestratorFactory: new ExportFactorySpy(), options: options);

        await orchestrator.ExportAsync(CreateExportContext(), CancellationToken.None);

        sourceFactory.Verify(f => f.CreateAsync(query, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ExportContext CreateExportContext() => new()
    {
        Job = new Job { JobId = "test-job", Kind = JobKind.Export },
        Package = PackageTestFactory.CreateLooseMock().Object,
        ProgressSink = Mock.Of<IProgressSink>(),
    };

    private sealed class ExportFactorySpy : IWorkItemExportOrchestratorFactory
    {
        public IAttachmentBinarySource? CapturedAttachmentSource { get; private set; }
        public IWorkItemFetchService? CapturedFetchService { get; private set; }
        public string? CapturedQuery { get; private set; }
        public IReadOnlyList<IModuleExtension>? CapturedExtensions { get; private set; }

        public IWorkItemExportOrchestrator Create(
            IPackageAccess package,
            string organisation,
            string project,
            ICheckpointingService checkpointingService,
            IAttachmentBinarySource? attachmentBinarySource,
            IProgressSink? progressSink,
            IWorkItemFetchService? fetchService,
            IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions,
            IPlatformMetrics? metrics,
            string? jobId,
            string? taskId,
            Microsoft.Extensions.Logging.ILogger? logger,
            string? wiqlQuery,
            IWorkItemDiscoveryService? discoveryService,
            IWorkItemProgressStoreFactory? exportProgressStoreFactory,
            string? packageUri,
            IReferencedPathLifecycle? referencedPathTracker = null,
            IReadOnlyList<IModuleExtension>? exportExtensions = null,
            MigrationEndpointOptions? endpoint = null,
            DevOpsMigrationPlatform.Abstractions.Agent.Attachments.IEmbeddedImageExportService? embeddedImageExport = null,
            DevOpsMigrationPlatform.Abstractions.Organisations.OrganisationEndpoint? sourceOrganisationEndpoint = null)
        {
            CapturedFetchService = fetchService;
            CapturedQuery = wiqlQuery;
            CapturedAttachmentSource = attachmentBinarySource;
            CapturedExtensions = exportExtensions;
            var stub = new Mock<IWorkItemExportOrchestrator>(MockBehavior.Loose);
            stub.Setup(o => o.ExportAsync(It.IsAny<IWorkItemRevisionSource>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            return stub.Object;
        }
    }
}
