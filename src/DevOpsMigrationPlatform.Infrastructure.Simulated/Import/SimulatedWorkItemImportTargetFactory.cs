// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Creates a <see cref="SimulatedWorkItemImportTarget"/> for endpoints with
/// <c>Type == "Simulated"</c>. No credentials are required.
/// </summary>
public sealed class SimulatedWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IOptions<SimulatedEndpointOptions> _options;

    public SimulatedWorkItemImportTargetFactory(IOptions<SimulatedEndpointOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct)
    {
        return Task.FromResult<IWorkItemImportTarget>(new SimulatedWorkItemImportTarget());
    }
}
