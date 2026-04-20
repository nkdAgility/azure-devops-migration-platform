namespace DevOpsMigrationPlatform.Abstractions.Telemetry;

/// <summary>
/// Classifies the data sensitivity of a log scope.
/// Used by the OTel log processor to filter customer-identifiable data from Azure Monitor
/// while allowing it to flow to package log files and control plane diagnostics.
/// </summary>
public enum DataClassification
{
    /// <summary>
    /// Operational/system logs (health checks, module lifecycle, job IDs).
    /// Safe to export to Azure Monitor. This is the default for unclassified logs.
    /// </summary>
    System = 0,

    /// <summary>
    /// Customer-identifiable data (work item IDs, field values, project names,
    /// org URLs, attachment paths). Blocked from Azure Monitor export.
    /// </summary>
    Customer = 1,

    /// <summary>
    /// Derived/aggregate data (counts, durations, averages).
    /// Safe to export to Azure Monitor.
    /// </summary>
    Derived = 2
}
