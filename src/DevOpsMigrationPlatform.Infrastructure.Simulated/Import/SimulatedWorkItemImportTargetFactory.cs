using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemImportTarget"/> for endpoints with
/// <c>Type == "Simulated"</c>.  No credentials are required.
/// Accepts <see cref="SimulatedEndpointOptions"/> (polymorphic config).
/// </summary>
public sealed class SimulatedWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));

        if (endpoint is not SimulatedEndpointOptions)
        {
            throw new ArgumentException(
                $"Expected {nameof(SimulatedEndpointOptions)} but received {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        return Task.FromResult<IWorkItemImportTarget>(new SimulatedWorkItemImportTarget());
    }
}
