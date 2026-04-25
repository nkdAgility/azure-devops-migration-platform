namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Result returned by <see cref="IWorkItemImportTarget"/> after creating or updating a work item.
/// </summary>
public record ImportedWorkItemResult
{
    /// <summary>The ID of the work item in the target system.</summary>
    public int TargetWorkItemId { get; init; }

    /// <summary>
    /// <see langword="true"/> if the work item was newly created;
    /// <see langword="false"/> if an existing target work item was updated.
    /// </summary>
    public bool IsNewlyCreated { get; init; }
}
