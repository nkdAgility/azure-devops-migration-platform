using System.Diagnostics;
using System.IO;
using OpenTelemetry;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry
{
    /// <summary>
    /// Writes exported <see cref="Activity"/> spans to a text file for agent diagnostics.
    /// 
    /// </summary>
    public sealed class FileTraceExporter : BaseExporter<Activity>
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        public FileTraceExporter(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
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



