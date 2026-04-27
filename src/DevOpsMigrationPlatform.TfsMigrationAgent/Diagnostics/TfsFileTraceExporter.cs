using System.Diagnostics;
using System.IO;
using OpenTelemetry;

namespace DevOpsMigrationPlatform.TfsMigrationAgent
{
    /// <summary>
    /// Writes exported <see cref="Activity"/> spans to a text file for TFS agent diagnostics.
    /// net481-specific inline implementation (ServiceDefaults is net10.0 only).
    /// </summary>
    internal sealed class TfsFileTraceExporter : BaseExporter<Activity>
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        public TfsFileTraceExporter(string filePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }

        public override ExportResult Export(in Batch<Activity> batch)
        {
            lock (_lock)
            {
                foreach (var activity in batch)
                {
                    _writer.WriteLine(
                        $"[{activity.StartTimeUtc:O}] {activity.Source.Name}/{activity.DisplayName} " +
                        $"({activity.Duration.TotalMilliseconds:F1}ms) TraceId={activity.TraceId} " +
                        $"SpanId={activity.SpanId} Status={activity.Status}");

                    foreach (var tag in activity.TagObjects)
                    {
                        _writer.WriteLine($"  {tag.Key}={tag.Value}");
                    }
                }
            }

            return ExportResult.Success;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
