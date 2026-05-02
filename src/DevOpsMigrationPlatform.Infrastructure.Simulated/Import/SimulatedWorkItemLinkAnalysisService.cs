// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Simulated implementation of <see cref="IWorkItemLinkAnalysisService"/>.
/// Returns an empty link analysis — no links, no dependencies.
/// No network calls are made.
/// </summary>
public sealed class SimulatedWorkItemLinkAnalysisService : IWorkItemLinkAnalysisService
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
        MigrationEndpointOptions endpoint,
        string project,
        string? wiqlFilter = null,
        BatchContinuationToken? savedContinuationToken = null,
        Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // No links in the simulated source — yield nothing.
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;
        yield break;
    }
}
