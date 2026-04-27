using System.Diagnostics;
using OpenTelemetry;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// Writes exported <see cref="Activity"/> spans to a text file in a human-readable format.
/// One line per span, with tags indented below. Useful for diagnosing whether the OTel
/// pipeline is producing traces before any remote exporter is involved.
/// </summary>
internal sealed class FileTraceExporter : BaseExporter<Activity>
{
    private readonly StreamWriter _writer;

    public FileTraceExporter(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public override ExportResult Export(in Batch<Activity> batch)
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
