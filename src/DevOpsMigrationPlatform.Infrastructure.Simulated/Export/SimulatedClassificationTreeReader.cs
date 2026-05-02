// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Export;

/// <summary>
/// Simulated implementation of <see cref="IClassificationTreeReader"/>.
/// Returns a minimal but realistic classification tree so the NodeTranslation source tree file is always written
/// and downstream import tests have data to verify against.
/// </summary>
public sealed class SimulatedClassificationTreeReader : IClassificationTreeReader
{
    private readonly ILogger<SimulatedClassificationTreeReader> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public SimulatedClassificationTreeReader(
        ILogger<SimulatedClassificationTreeReader> logger,
        ISourceEndpointInfo endpointInfo)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var project = _endpointInfo.Project;
        if (string.IsNullOrEmpty(project)) yield break;

        _logger.LogDebug("[NodeTranslation][Simulated] Yielding area nodes for {Project}.", project);

        // Project root
        yield return project;
        yield return $"{project}\\Team A";
        yield return $"{project}\\Team A\\Sprint 1";
        yield return $"{project}\\Team B";
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var project = _endpointInfo.Project;
        if (string.IsNullOrEmpty(project)) yield break;

        _logger.LogDebug("[NodeTranslation][Simulated] Yielding iteration nodes for {Project}.", project);

        var baseDate = new System.DateTimeOffset(2024, 1, 1, 0, 0, 0, System.TimeSpan.Zero);

        yield return new IterationNodeEntry(project, null, null, false);
        yield return new IterationNodeEntry($"{project}\\Iteration 1", baseDate, baseDate.AddDays(14), false);
        yield return new IterationNodeEntry($"{project}\\Iteration 2", baseDate.AddDays(14), baseDate.AddDays(28), false);
    }
}
