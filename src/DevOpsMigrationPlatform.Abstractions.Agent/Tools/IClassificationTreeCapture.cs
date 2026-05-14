// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Captures the full classification tree from the source and writes it to the package.
/// </summary>
public interface IClassificationTreeCapture
{
    /// <summary>
    /// Captures the source classification tree and writes <c>Nodes/source-tree.json</c>.
    /// </summary>
    /// <returns>Total number of nodes captured (area + iteration).</returns>
    Task<int> CaptureAsync(
        IArtefactStore artefactStore,
        CancellationToken ct,
        IPlatformMetrics? metrics = null,
        string? jobId = null,
        IProgressSink? sink = null,
        string moduleName = "Nodes"
    );
}
