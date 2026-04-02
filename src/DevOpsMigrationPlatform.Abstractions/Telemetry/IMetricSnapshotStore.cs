namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// In-process store for the most recent <see cref="MetricSnapshot"/>.
/// Updated by <c>SnapshotMetricExporter</c> on every OTel export cycle.
/// Read by <c>ControlPlaneTelemetryTimer</c> to push to the Control Plane.
/// </summary>
public interface IMetricSnapshotStore
{
    /// <summary>Replaces the stored snapshot with the given value.</summary>
    void Update(MetricSnapshot snapshot);

    /// <summary>Returns the most recent snapshot, or <c>null</c> if none has been recorded yet.</summary>
    MetricSnapshot? Latest { get; }
}
