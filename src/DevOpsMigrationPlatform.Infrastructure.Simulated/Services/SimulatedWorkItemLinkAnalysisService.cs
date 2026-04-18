using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // No links in the simulated source — yield nothing.
        cancellationToken.ThrowIfCancellationRequested();
        await System.Threading.Tasks.Task.CompletedTask;
        yield break;
    }
}
