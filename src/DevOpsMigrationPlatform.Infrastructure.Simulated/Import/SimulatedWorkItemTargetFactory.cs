// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemTarget"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// </summary>
public sealed class SimulatedWorkItemTargetFactory : IWorkItemTargetFactory
{
    private readonly IOptions<SimulatedEndpointOptions> _options;

    public SimulatedWorkItemTargetFactory(IOptions<SimulatedEndpointOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public Task<IWorkItemTarget> CreateAsync(CancellationToken ct)
    {
        var configuredTypes = _options.Value.Generator.Projects
            .SelectMany(project => project.WorkItemTypes)
            .Select(workItemType => workItemType.Type)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        IWorkItemTarget target = configuredTypes.Length == 0
            ? new SimulatedWorkItemTarget()
            : new SimulatedWorkItemTarget(configuredTypes);

        return Task.FromResult(target);
    }
}
