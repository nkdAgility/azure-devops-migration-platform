namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// OpenTelemetry instrument name constants for Application Insights contract stability.
/// These names MUST remain stable to preserve historical telemetry data continuity.
/// Any changes require migration documentation and backward compatibility strategy.
/// </summary>
public static class WellKnownMetricNames
{
    // WorkItem Export Metrics
    public const string WorkItemsExported = "work_item_exported_total";
    public const string RevisionsExported = "revision_exported_total";
    public const string RevisionErrors = "revision_export_errors_total";
    public const string LinksExported = "link_exported_total";
    public const string LinkErrors = "link_export_errors_total";
    public const string WorkItemDuration = "work_item_export_duration_ms";
    public const string RevisionDuration = "revision_export_duration_ms";
    public const string LinkDuration = "link_export_duration_ms";
    public const string TotalDuration = "export_total_duration_ms";

    // Attachment Download Metrics  
    public const string AttachmentAttempts = "attachment_download_attempt_total";
    public const string AttachmentSuccesses = "attachment_download_success_total";
    public const string AttachmentFailures = "attachment_download_failure_total";
    public const string AttachmentDuration = "attachment_download_duration_ms";
}