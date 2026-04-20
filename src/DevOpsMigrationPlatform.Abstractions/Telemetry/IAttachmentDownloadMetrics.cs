using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Records telemetry for attachment download operations.
/// </summary>
[System.Obsolete("Use IMigrationMetrics. Will be removed when all call sites are migrated.")]
public interface IAttachmentDownloadMetrics
{
    void RecordAttempt();
    void RecordSuccess();
    void RecordFailure();
    void RecordDuration(TimeSpan duration);
}
