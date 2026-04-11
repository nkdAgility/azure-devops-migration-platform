using System.Collections.Generic;
using System.Threading;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Exports work items from a source TFS / Azure DevOps Server into the migration package.
/// Yields <see cref="WorkItemMigrationProgress"/> updates for each unit of work.
/// Implementations must stream — no buffering all revisions into memory.
/// </summary>
public interface IWorkItemExportService
{
    /// <summary>
    /// Exports all work items matching <paramref name="wiqlQuery"/> from
    /// <paramref name="project"/> on <paramref name="tfsServer"/> and writes each
    /// revision to the package via <see cref="IArtefactStore"/>.
    /// </summary>
    IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(
        string tfsServer,
        string project,
        string wiqlQuery,
        IProgressSink progressSink,
        CancellationToken cancellationToken = default);
}
