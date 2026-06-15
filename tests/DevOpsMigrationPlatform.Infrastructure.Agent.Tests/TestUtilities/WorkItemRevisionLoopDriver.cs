// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

/// <summary>
/// Test-only driver that routes the per-job pre-created resources into
/// <see cref="WorkItemsOrchestrator.RunRevisionFolderLoopAsync"/> without requiring
/// a full <see cref="ImportContext"/> setup.
/// Replaces the deleted secondary-ctor entry point of WorkItemsImportRuntime.
/// </summary>
public sealed class WorkItemRevisionLoopDriver
{
    private readonly WorkItemsOrchestrator _orchestrator;
    private readonly WorkItemRevisionJobScope _scope;

    internal WorkItemRevisionLoopDriver(WorkItemRevisionJobScope scope, WorkItemsOrchestrator? orchestrator = null)
    {
        _scope = scope;
        _orchestrator = orchestrator ?? CreateMinimalOrchestrator();
    }

    public Task ImportAsync(WorkItemsModuleExtensions ext, ResumeMode resumeMode, CancellationToken ct)
        => _orchestrator.RunRevisionFolderLoopAsync(_scope, ext, resumeMode, ct);

    internal static WorkItemsOrchestrator CreateMinimalOrchestrator()
    {
        var options = Options.Create(new WorkItemsModuleOptions());
        return new WorkItemsOrchestrator(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            null,
            null,
            null,
            Mock.Of<IWorkItemExportOrchestratorFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            NullLogger<WorkItemsModule>.Instance,
            null,
            null,
            null,
            null,
            options,
            Mock.Of<ISourceEndpointInfo>(),
            new ImportPreparer(options, "org", "project", []),
            Mock.Of<IWorkItemTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            null,
            Mock.Of<IWorkItemsImportCapabilityValidator>(),
            Mock.Of<IWorkItemsNodeReadinessOrchestrator>(),
            Mock.Of<ITargetEndpointInfo>());
    }
}
