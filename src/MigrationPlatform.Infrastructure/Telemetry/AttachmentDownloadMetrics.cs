using MigrationPlatform.Abstractions.Telemetry;
using System.Diagnostics.Metrics;

namespace MigrationPlatform.Infrastructure.Telemetry
{
    public class AttachmentDownloadMetrics : IAttachmentDownloadMetrics
    {
        public const string MeterName = "MigrationPlatform.AttachmentDownload";
        public const string MeterVersion = "1.0";

        private static readonly Meter _meter = new Meter(MeterName, MeterVersion);

        private static readonly Counter<long> _downloadAttemptCounter =
            _meter.CreateCounter<long>("attachment_download_attempt_total", description: "Total number of attachment download attempts.");

        private static readonly Counter<long> _downloadSuccessCounter =
            _meter.CreateCounter<long>("attachment_download_success_total", description: "Total number of successful attachment downloads.");

        private static readonly Counter<long> _downloadFailureCounter =
            _meter.CreateCounter<long>("attachment_download_failure_total", description: "Total number of failed attachment downloads.");

        private static readonly Histogram<double> _downloadDurationHistogram =
            _meter.CreateHistogram<double>("attachment_download_duration_ms", unit: "ms", description: "Duration of attachment downloads in milliseconds.");

        public void RecordAttempt()
        {
            _downloadAttemptCounter.Add(1);
        }

        public void RecordSuccess()
        {
            _downloadSuccessCounter.Add(1);
        }

        public void RecordFailure()
        {
            _downloadFailureCounter.Add(1);
        }

        public void RecordDuration(TimeSpan duration)
        {
            _downloadDurationHistogram.Record(duration.TotalMilliseconds);
        }
    }
}
