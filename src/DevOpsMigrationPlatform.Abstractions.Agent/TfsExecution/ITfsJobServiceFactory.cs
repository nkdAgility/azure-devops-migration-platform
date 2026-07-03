// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.TfsExecution;

/// <summary>
/// Canonical port for creating per-job TFS services from a <see cref="MigrationEndpointOptions"/>
/// (ADR-0023 / CA-H1, HX-M1). The concrete factory lives in Infrastructure.TfsObjectModel;
/// the TFS job worker depends only on this contract.
/// </summary>
public interface ITfsJobServiceFactory
{
    /// <summary>
    /// Creates a scoped set of TFS services for a single job.
    /// The caller MUST dispose the returned <see cref="ITfsJobServices"/> after the job completes.
    /// </summary>
    ITfsJobServices CreateForEndpoint(MigrationEndpointOptions endpoint);
}

/// <summary>
/// Contract view over the per-job TFS service set. Exposes only Abstractions-owned
/// seam types — TFS SDK types (e.g. the work item store) remain on the concrete
/// implementation inside Infrastructure.TfsObjectModel.
/// Disposing releases the underlying TFS collection connection.
/// </summary>
public interface ITfsJobServices : IDisposable
{
    IWorkItemRevisionSource RevisionSource { get; }
    IAttachmentBinarySource AttachmentSource { get; }
    INodeCreator NodeCreator { get; }
    IClassificationTreeReader ClassificationTreeReader { get; }
    IWorkItemDiscoveryService DiscoveryService { get; }
    IProjectDiscoveryService ProjectDiscoveryService { get; }
    IWorkItemFetchService FetchService { get; }
    MigrationEndpointOptions Endpoint { get; }
    IIdentitySource IdentitySource { get; }
    ITeamSource TeamSource { get; }
    IProjectLifecycleService ProjectLifecycleService { get; }
    IWorkItemExportMetrics ExportMetrics { get; }
    IAttachmentDownloadMetrics AttachmentMetrics { get; }
}
