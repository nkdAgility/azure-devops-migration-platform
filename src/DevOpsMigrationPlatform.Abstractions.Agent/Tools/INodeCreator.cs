using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Creates and queries classification nodes in the target ADO project.
/// All methods are idempotent and retryable.
/// </summary>
public interface INodeCreator
{
    /// <summary>Checks whether a classification node exists in the target identified by <paramref name="endpoint"/>.</summary>
    Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        MigrationEndpointOptions endpoint,
        CancellationToken ct);

    /// <summary>
    /// Ensures a node exists in the target identified by <paramref name="endpoint"/>.
    /// Creates it (and all ancestors) if missing.
    /// Idempotent: returns successfully if node already exists.
    /// </summary>
    Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        MigrationEndpointOptions endpoint,
        CancellationToken ct);

    /// <summary>
    /// Sets iteration start/finish dates on a target node identified by <paramref name="endpoint"/>.
    /// No-op if both dates are null.
    /// </summary>
    Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        MigrationEndpointOptions endpoint,
        CancellationToken ct);
}
