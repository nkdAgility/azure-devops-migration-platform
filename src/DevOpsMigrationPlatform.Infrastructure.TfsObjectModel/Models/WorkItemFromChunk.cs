using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Models;

/// <summary>
/// A work item returned as part of a date-range query chunk.
/// Extends <see cref="WorkItemQueryChunk"/> with the actual <see cref="WorkItem"/> reference.
/// </summary>
public class WorkItemFromChunk : WorkItemQueryChunk
{
    public WorkItem WorkItem { get; set; } = default!;
}
