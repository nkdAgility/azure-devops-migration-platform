using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Progress update yielded by <see cref="IWorkItemExportService.ExportWorkItemsAsync"/>
/// after each unit of work. Consumers display or log this to track progress.
/// </summary>
public class WorkItemMigrationProgress
{
    public int WorkItemId { get; set; }
    public int RevisionIndex { get; set; }

    public int AttachmentsProcessed { get; set; }
    public int LinksProcessed { get; set; }
    public int FieldsProcessed { get; set; }

    public int TotalWorkItems { get; set; }
    public int WorkItemsProcessed { get; set; }

    public int TotalRevisions { get; set; }
    public int RevisionsProcessed { get; set; }

    public string? Message { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public WorkItemQueryChunk? ChunkInfo { get; set; }

    public int AttachmentsFailed { get; set; }
}
