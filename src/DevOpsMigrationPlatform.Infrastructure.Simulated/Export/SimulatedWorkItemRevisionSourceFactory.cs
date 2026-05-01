using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemRevisionSource"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// Accepts <see cref="SimulatedEndpointOptions"/> from DI.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IOptions<SimulatedEndpointOptions> _options;

    public SimulatedWorkItemRevisionSourceFactory(IOptions<SimulatedEndpointOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(CancellationToken cancellationToken)
    {
        var simOpts = _options.Value;
        return Task.FromResult<IWorkItemRevisionSource>(
            new SimulatedWorkItemRevisionSource(simOpts.Generator));
    }
}
