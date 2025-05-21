namespace MigrationPlatform.Abstractions.Telemetry
{
    public interface IAttachmentDownloadMetrics
    {
        void RecordAttempt();
        void RecordSuccess();
        void RecordFailure();
        void RecordDuration(TimeSpan duration);
    }
}
