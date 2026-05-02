// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry
{
    /// <summary>
    /// Writes exported <see cref="Metric"/> snapshots to a text file for agent diagnostics.
    /// </summary>
    public sealed class DiagnosticsFileMetricExporter : BaseExporter<Metric>
    {
        private readonly StreamWriter _writer;
        private readonly object _lock = new object();

        public DiagnosticsFileMetricExporter(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null && dir.Length > 0)
                Directory.CreateDirectory(dir);
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream) { AutoFlush = true };
        }

        public override ExportResult Export(in Batch<Metric> batch)
        {
            lock (_lock)
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



