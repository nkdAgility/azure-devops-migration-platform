using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Abstraction for exporting work item comments to the migration package.
/// Orchestrates comment source fetching, file packaging, and cursor management.
/// </summary>
public interface IWorkItemCommentExportService
{
    /// <summary>
    /// Exports all comments for a single work item.
    /// Each comment version is written to a separate folder under WorkItems/yyyy-MM-dd/.
    /// </summary>
    /// <param name="workItemId">The work item ID.</param>
    /// <param name="organisationUrl">The Azure DevOps organization URL.</param>
    /// <param name="project">The project name.</param>
    /// <param name="pat">The personal access token for authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async export operation.</returns>
    Task ExportAsync(int workItemId, string organisationUrl, string project, string pat, CancellationToken cancellationToken);
}
