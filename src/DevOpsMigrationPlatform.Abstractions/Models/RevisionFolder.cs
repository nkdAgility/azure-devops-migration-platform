using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Represents one revision written to the package.
/// The folder name is canonical: yyyy-MM-dd/&lt;ticks&gt;-&lt;workItemId&gt;-&lt;revisionIndex&gt;/
/// </summary>
public record RevisionFolder
{
    public int WorkItemId { get; init; }
    public int RevisionIndex { get; init; }
    public DateTimeOffset ChangedDate { get; init; }

    /// <summary>
    /// The canonical folder path relative to the package root.
    /// e.g. "WorkItems/2024-01-15/00638412345678-42-1/"
    /// </summary>
    public string FolderPath { get; init; } = string.Empty;
}
