using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.CLI.JobRunners;

public interface ILogsClient
{
    Task<IReadOnlyList<ProgressEvent>> GetLogsAsync(Guid jobId, CancellationToken ct);
    IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct);
}
