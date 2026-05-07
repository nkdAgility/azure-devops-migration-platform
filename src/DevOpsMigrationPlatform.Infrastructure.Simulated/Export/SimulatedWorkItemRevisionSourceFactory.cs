// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemRevisionSource"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// Reads the <see cref="SimulatedGeneratorConfig"/> from the current job's Source
/// via <see cref="ICurrentPackageConfigAccessor.Current"/> so that the Generator (including Projects)
/// always reflects the per-job migration-config.json rather than a stale singleton value.
/// </summary>
public sealed class SimulatedWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly ICurrentPackageConfigAccessor _packageConfigAccessor;

    public SimulatedWorkItemRevisionSourceFactory(ICurrentPackageConfigAccessor packageConfigAccessor)
    {
        _packageConfigAccessor = packageConfigAccessor ?? throw new ArgumentNullException(nameof(packageConfigAccessor));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(CancellationToken cancellationToken)
    {
        var generator = new SimulatedGeneratorConfig();
        _packageConfigAccessor.Current?
            .GetSection("MigrationPlatform:Source:Generator")
            .Bind(generator);
        return Task.FromResult<IWorkItemRevisionSource>(
            new SimulatedWorkItemRevisionSource(generator));
    }
}
