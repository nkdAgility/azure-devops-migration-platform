using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Creates and queries classification nodes in the target ADO project.
/// All methods are idempotent and retryable.
/// </summary>
public interface INodeCreator
{
    /// <summary>Checks whether a classification node exists in the target.</summary>
    Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    /// <summary>
    /// Ensures a node exists in the target. Creates it (and all ancestors) if missing.
    /// Idempotent: returns successfully if node already exists.
    /// </summary>
    Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    /// <summary>
    /// Sets iteration start/finish dates on a target node.
    /// No-op if both dates are null.
    /// </summary>
    Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct);
}
