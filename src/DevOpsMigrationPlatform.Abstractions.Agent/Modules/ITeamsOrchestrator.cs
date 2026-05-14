// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates team export, import, and validation operations.
/// </summary>
public interface ITeamsOrchestrator
{
    Task ExportAsync(
        ITeamSource teamSource,
        ExportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct);

    Task ImportAsync(
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct);

    Task ValidateAsync(
        IArtefactStore artefactStore,
        ValidationContext context,
        CancellationToken ct);
}
#endif
