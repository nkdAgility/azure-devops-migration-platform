using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Tracks how far the export has progressed for each work item.
/// Watermarks only advance — they never retreat.
/// </summary>
public interface IWorkItemWatermarkStore
{
    /// <summary>
    /// Records that <paramref name="revisionIndex"/> has been successfully processed
    /// for <paramref name="workItemId"/>. Only advances the watermark; if the stored
    /// value is already >= <paramref name="revisionIndex"/> the call is a no-op.
    /// </summary>
    Task UpdateWatermarkAsync(int workItemId, int revisionIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the highest revision index recorded for <paramref name="workItemId"/>,
    /// or <c>null</c> if the work item has never been exported.
    /// </summary>
    Task<int?> GetWatermarkAsync(int workItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> if the watermark for <paramref name="workItemId"/> is
    /// greater than or equal to <paramref name="revisionIndex"/>.
    /// </summary>
    Task<bool> IsRevisionProcessedAsync(int workItemId, int revisionIndex, CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="count"/> as the cached result of <paramref name="query"/>.
    /// Overwrites any existing cached value.
    /// </summary>
    Task UpdateQueryCountAsync(string query, int count, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the cached count for <paramref name="query"/>, or <c>null</c> if no
    /// count has been stored for that query.
    /// </summary>
    Task<int?> GetQueryCountAsync(string query, CancellationToken cancellationToken);
}
