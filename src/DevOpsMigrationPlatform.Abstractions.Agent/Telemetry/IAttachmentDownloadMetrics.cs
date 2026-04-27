using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Records telemetry for attachment download operations.
/// Used by the net481 TFS path where <c>IMigrationMetrics</c> (which requires <c>TagList</c>) is unavailable.
/// </summary>
public interface IAttachmentDownloadMetrics
{
    void RecordAttempt();
    void RecordSuccess();
    void RecordFailure();
    void RecordDuration(TimeSpan duration);
}
