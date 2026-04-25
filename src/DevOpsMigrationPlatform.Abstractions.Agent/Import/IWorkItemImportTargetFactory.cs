using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Factory for creating <see cref="IWorkItemImportTarget"/> instances from endpoint options.
/// Mirrors <see cref="IWorkItemRevisionSourceFactory"/> on the export side.
/// </summary>
public interface IWorkItemImportTargetFactory
{
    /// <summary>
    /// Creates an import target for the given endpoint.
    /// </summary>
    /// <param name="endpoint">Endpoint options; concrete type determines the connector used.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IWorkItemImportTarget> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct);
}
