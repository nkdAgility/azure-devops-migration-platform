using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemQueryWindowStrategy"/>.
/// Uses date-range WIQL chunking via <see cref="WorkItemStoreExtensions.QueryAllByDateChunk"/>
/// to keep each query under the TFS 20,000-item limit.
///
/// The <paramref name="url"/> and <paramref name="pat"/> parameters are ignored —
/// the injected <see cref="WorkItemStore"/> is already authenticated.
/// The <paramref name="project"/> parameter is used only to build the default WIQL
/// query when <see cref="WorkItemQueryWindowOptions.BaseQuery"/> is not supplied.
/// </summary>
public sealed class TfsWorkItemQueryWindowStrategy : IWorkItemQueryWindowStrategy
{
    private readonly WorkItemStore _workItemStore;
    private readonly ILogger<TfsWorkItemQueryWindowStrategy> _logger;

    public TfsWorkItemQueryWindowStrategy(
        WorkItemStore workItemStore,
        ILogger<TfsWorkItemQueryWindowStrategy> logger)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemQueryWindow> EnumerateWindowsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemQueryWindowOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Yield control so callers composing async pipelines are not blocked synchronously.
        await System.Threading.Tasks.Task.Yield();

        var baseQuery = options?.BaseQuery is { Length: > 0 }
            ? options.BaseQuery
            : $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{EscapeWiql(project)}'";

        var progressSink = new LoggingProgressSink(_logger);

        int currentQueryIndex = -1;
        var windowIds = new List<int>();
        DateTime windowStart = DateTime.MinValue;
        DateTime windowEnd = DateTime.MinValue;

        foreach (var item in _workItemStore.QueryAllByDateChunk(baseQuery, progressSink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.QueryIndex != currentQueryIndex)
            {
                if (windowIds.Count > 0)
                {
                    yield return new WorkItemQueryWindow
                    {
                        WindowStart = windowStart,
                        WindowEnd = windowEnd,
                        WindowSize = windowEnd - windowStart,
                        WorkItemIds = windowIds.ToArray()
                    };
                    windowIds.Clear();
                }

                currentQueryIndex = item.QueryIndex;
                windowStart = item.ChunkStart;
                windowEnd = item.ChunkEnd;
            }

            windowIds.Add(item.WorkItem.Id);
        }

        if (windowIds.Count > 0)
        {
            yield return new WorkItemQueryWindow
            {
                WindowStart = windowStart,
                WindowEnd = windowEnd,
                WindowSize = windowEnd - windowStart,
                WorkItemIds = windowIds.ToArray()
            };
        }
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''");

    private sealed class LoggingProgressSink : IProgressSink
    {
        private readonly ILogger _logger;

        public LoggingProgressSink(ILogger logger) => _logger = logger;

        public void Emit(ProgressEvent evt) =>
            _logger.LogInformation("[{Module}/{Stage}] {Message}", evt.Module, evt.Stage, evt.Message);
    }
}
