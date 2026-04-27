using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Diagnostics;

/// <summary>
/// Writes exported <see cref="Metric"/> snapshots to a text file in a human-readable format.
/// One line per metric point. Useful for diagnosing whether the OTel pipeline is
/// producing metrics before any remote exporter is involved.
/// </summary>
internal sealed class FileMetricExporter : BaseExporter<Metric>
{
    private readonly StreamWriter _writer;

    public FileMetricExporter(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        foreach (var metric in batch)
        {
            foreach (ref readonly var point in metric.GetMetricPoints())
            {
                _writer.Write($"[{point.EndTime:O}] {metric.MeterName}/{metric.Name}");

                switch (metric.MetricType)
                {
                    case MetricType.LongSum:
                        _writer.Write($" Sum={point.GetSumLong()}");
                        break;
                    case MetricType.DoubleSum:
                        _writer.Write($" Sum={point.GetSumDouble()}");
                        break;
                    case MetricType.LongGauge:
                        _writer.Write($" Gauge={point.GetGaugeLastValueLong()}");
                        break;
                    case MetricType.DoubleGauge:
                        _writer.Write($" Gauge={point.GetGaugeLastValueDouble()}");
                        break;
                    case MetricType.Histogram:
                        _writer.Write($" Count={point.GetHistogramCount()} Sum={point.GetHistogramSum()}");
                        break;
                    default:
                        _writer.Write($" ({metric.MetricType})");
                        break;
                }

                foreach (var tag in point.Tags)
                {
                    _writer.Write($" {tag.Key}={tag.Value}");
                }

                _writer.WriteLine();
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
