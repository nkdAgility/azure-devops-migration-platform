namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// In-process store for the most recent <see cref="JobSnapshot"/>.
/// Updated by discovery and migration modules at project boundaries.
/// Read by <see cref="IControlPlaneTelemetryClient"/> push timer to forward to the Control Plane.
/// </summary>
public interface IJobSnapshotStore
{
    /// <summary>Replaces the stored snapshot with the given value.</summary>
    void Update(JobSnapshot snapshot);

    /// <summary>Returns the most recent snapshot, or <c>null</c> if none has been recorded yet.</summary>
    JobSnapshot? Latest { get; }
}
