using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Factory for creating <see cref="IWorkItemImportTarget"/> instances from job configuration.
/// Mirrors <see cref="IWorkItemRevisionSourceFactory"/> on the export side.
/// </summary>
public interface IWorkItemImportTargetFactory
{
    /// <summary>
    /// Creates an import target connected to the given Azure DevOps organisation and project.
    /// </summary>
    Task<IWorkItemImportTarget> CreateAsync(
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct);
}
