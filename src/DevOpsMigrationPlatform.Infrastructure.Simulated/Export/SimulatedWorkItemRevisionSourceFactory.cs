using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemRevisionSource"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// Reads the <see cref="SimulatedGeneratorConfig"/> from the current job's Source
/// via <see cref="ActiveJobConfigState.PackageConfig"/> so that the Generator (including Projects)
/// always reflects the per-job migration-config.json rather than a stale singleton value.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly ActiveJobConfigState _activeJobConfig;

    public SimulatedWorkItemRevisionSourceFactory(ActiveJobConfigState activeJobConfig)
    {
        _activeJobConfig = activeJobConfig ?? throw new ArgumentNullException(nameof(activeJobConfig));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(CancellationToken cancellationToken)
    {
        var generator = new SimulatedGeneratorConfig();
        _activeJobConfig.PackageConfig?
            .GetSection("MigrationPlatform:Source:Generator")
            .Bind(generator);
        return Task.FromResult<IWorkItemRevisionSource>(
            new SimulatedWorkItemRevisionSource(generator));
    }
}
