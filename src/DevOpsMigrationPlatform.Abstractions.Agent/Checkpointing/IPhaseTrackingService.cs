using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Tracks and persists the overall job phase (Export / Import completed flags)
/// for <c>Both</c>-mode jobs, enabling resume across process restarts.
/// </summary>
public interface IPhaseTrackingService
{
    /// <summary>
    /// Reads the current phase record from the state store.
    /// Returns a default (all-false) record when no record exists yet.
    /// </summary>
    Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the given phase record to the state store.
    /// </summary>
    Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the phase record from the state store.
    /// No-op if no record exists.
    /// </summary>
    Task DeletePhaseRecordAsync(CancellationToken cancellationToken);
}
