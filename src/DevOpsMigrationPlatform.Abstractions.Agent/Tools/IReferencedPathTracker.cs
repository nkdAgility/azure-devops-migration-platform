// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Tracks area and iteration paths referenced by work items during export.
/// Persists to <c>Nodes/referenced-paths.json</c>.
/// </summary>
public interface IReferencedPathTracker
{
    /// <summary>Loads existing state from the artefact store (for resume). Call once before discovery begins.</summary>
    Task InitializeAsync(IArtefactStore artefactStore, CancellationToken ct);

    /// <summary>Records a discovered area path. If new, persists the artifact.</summary>
    Task RecordAreaPathAsync(string path, IArtefactStore artefactStore, CancellationToken ct);

    /// <summary>Records a discovered iteration path. If new, persists the artifact.</summary>
    Task RecordIterationPathAsync(string path, IArtefactStore artefactStore, CancellationToken ct);
}
#endif
