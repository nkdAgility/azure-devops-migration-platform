using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemImportTarget"/> for endpoints with
/// <c>Type == "Simulated"</c>.  No credentials are required.
/// Accepts both <see cref="SimulatedEndpointOptions"/> (polymorphic config) and
/// <see cref="JobEndpointMigrationOptions"/> (job contract bridge) until the
/// JobEndpoint adapter is eliminated.
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

        if (endpoint is not SimulatedEndpointOptions and not JobEndpointMigrationOptions)
        {
            throw new ArgumentException(
                $"Expected {nameof(SimulatedEndpointOptions)} or {nameof(JobEndpointMigrationOptions)} " +
                $"but received {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        return Task.FromResult<IWorkItemImportTarget>(new SimulatedWorkItemImportTarget());
    }
}
