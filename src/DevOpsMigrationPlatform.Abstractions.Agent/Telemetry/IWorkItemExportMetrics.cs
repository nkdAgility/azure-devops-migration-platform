using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Records telemetry for work item export operations.
/// Implementations use OpenTelemetry meters and histograms.
/// Used by the net481 TFS path where <c>IMigrationMetrics</c> (which requires <c>TagList</c>) is unavailable.
/// </summary>
public interface IWorkItemExportMetrics
{
    void RecordWorkItemExported(System.Guid teamProjectCollectionId);
    void RecordRevisionExported(System.Guid teamProjectCollectionId, int workItemId);
    void RecordWorkItemProcessingDuration(System.Guid teamProjectCollectionId, TimeSpan duration);
    void RecordRevisionProcessingDuration(System.Guid teamProjectCollectionId, int workItemId, TimeSpan duration);
    void RecordProcessingDuration(TimeSpan duration);
    void RecordRevisionError(System.Guid teamProjectCollectionId, int workItemId);
    void RecordLinkExported(System.Guid teamProjectCollectionId, int workItemId, int revisionIndex);
    void RecordLinkProcessingDuration(System.Guid teamProjectCollectionId, int workItemId, int revisionIndex, TimeSpan duration);
    void RecordLinkError(System.Guid teamProjectCollectionId, int workItemId, int revisionIndex);
}
