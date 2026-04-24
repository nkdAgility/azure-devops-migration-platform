using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemRevisionSource"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// Accepts <see cref="SimulatedEndpointOptions"/> carrying the generator config.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken cancellationToken)
    {
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));

        if (endpoint is not SimulatedEndpointOptions simOpts)
        {
            throw new ArgumentException(
                $"Expected {nameof(SimulatedEndpointOptions)} but received {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        return Task.FromResult<IWorkItemRevisionSource>(
            new SimulatedWorkItemRevisionSource(simOpts.Generator));
    }
}
