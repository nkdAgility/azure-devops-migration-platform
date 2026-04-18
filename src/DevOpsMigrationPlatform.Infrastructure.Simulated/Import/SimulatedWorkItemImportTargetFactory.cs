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

        if (endpoint is not SimulatedEndpointOptions &&
            !string.Equals(endpoint.Type, "Simulated", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Expected {nameof(SimulatedEndpointOptions)} or endpoint with Type='Simulated', " +
                $"got {endpoint.GetType().Name} with Type='{endpoint.Type}'.",
                nameof(endpoint));
        }

        return Task.FromResult<IWorkItemImportTarget>(new SimulatedWorkItemImportTarget());
    }
}
