// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates identity descriptor export, import (lookup/resolution), and validation.
/// </summary>
public interface IIdentitiesOrchestrator
{
    Task ExportAsync(
        IIdentitySource identitySource,
        ExportContext context,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

#if !NET481
    Task ImportAsync(
        IIdentityLookupTool? identityLookupTool,
        ImportContext context,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);
#endif

    Task ValidateAsync(
        IArtefactStore artefactStore,
        ValidationContext context,
        CancellationToken ct);
}
