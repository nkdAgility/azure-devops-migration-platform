// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

public sealed class SimulatedWorkItemTypeReadinessTargetFactory : IWorkItemTypeReadinessTargetFactory
{
    private readonly IOptions<SimulatedEndpointOptions> _options;

    public SimulatedWorkItemTypeReadinessTargetFactory(IOptions<SimulatedEndpointOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<IWorkItemTypeReadinessTarget> CreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var configuredTypes = _options.Value.Generator.Projects
            .SelectMany(project => project.WorkItemTypes)
            .Select(workItemType => workItemType.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IWorkItemTypeReadinessTarget target = new SimulatedWorkItemTypeReadinessTarget(configuredTypes);
        return Task.FromResult(target);
    }
}
