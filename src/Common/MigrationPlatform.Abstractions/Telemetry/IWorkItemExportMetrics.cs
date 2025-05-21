namespace MigrationPlatform.Abstractions.Telemetry
{
    public interface IWorkItemExportMetrics
    {
        void RecordWorkItemExported(Guid teamProjectCollectionId);
        void RecordRevisionExported(Guid teamProjectCollectionId, int workItemId);
        void RecordWorkItemProcessingDuration(Guid teamProjectCollectionId, TimeSpan duration);
        void RecordRevisionProcessingDuration(Guid teamProjectCollectionId, int workItemId, TimeSpan duration);

        void RecordProcessingDuration(TimeSpan duration);

        void RecordRevisionError(Guid teamProjectCollectionId, int workItemId);

        void RecordLinkExported(Guid teamProjectCollectionId, int workItemId, int revisionIndex);
        void RecordLinkProcessingDuration(Guid teamProjectCollectionId, int workItemId, int revisionIndex, TimeSpan duration);
        void RecordLinkError(Guid teamProjectCollectionId, int workItemId, int revisionIndex);

    }
}
