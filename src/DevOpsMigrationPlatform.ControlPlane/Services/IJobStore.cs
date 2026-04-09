using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// Persistence contract for <see cref="MigrationJob"/> instances submitted to the control plane.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Stores a submitted job and enqueues it for agent pickup.
    /// Returns the job id.
    /// </summary>
    Guid Enqueue(MigrationJob job);

    /// <summary>
    /// Dequeues one pending job, waiting up to <paramref name="timeout"/>.
    /// Returns <c>null</c> if no job became available within the timeout.
    /// </summary>
    Task<MigrationJob?> DequeueAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a snapshot of all submitted jobs.
    /// </summary>
    IReadOnlyList<MigrationJob> GetAll();

    /// <summary>
    /// Returns the job with the given id, or <c>null</c> if not found.
    /// </summary>
    MigrationJob? Get(Guid jobId);
}
