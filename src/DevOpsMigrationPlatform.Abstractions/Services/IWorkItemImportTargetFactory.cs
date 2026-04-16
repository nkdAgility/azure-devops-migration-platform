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
    /// Creates an import target for the given endpoint.
    /// </summary>
    /// <param name="targetType">
    /// The target type declared in the scenario config (e.g. <c>"AzureDevOpsServices"</c>,
    /// <c>"Simulated"</c>). Used by routing factories to select the correct implementation.
    /// </param>
    Task<IWorkItemImportTarget> CreateAsync(
        string targetType,
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct);
}
