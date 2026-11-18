// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions.Extensions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

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

        var baseQuery = ToWindowBaseQuery(options?.BaseQuery, project);

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

    /// <summary>
    /// The query the date windows are appended to ("AND [System.CreatedDate] ..."): the
    /// configured query's WHERE in brackets, with <c>@project</c> resolved (an Object Model query
    /// has no project context) and ORDER BY removed. No query means the whole project (#171).
    /// </summary>
    internal static string ToWindowBaseQuery(string? query, string project)
    {
        string where;
        if (string.IsNullOrWhiteSpace(query))
        {
            where = "[System.TeamProject] = @project";
        }
        else
        {
            // Keywords count only at the top level: not inside a quoted literal or a bracketed
            // field name, and on any whitespace (PR #222 review).
            var q = query!.Trim();
            var whereAt = FindTopLevelKeyword(q, "WHERE", 0);
            if (whereAt < 0)
                throw new InvalidOperationException(
                    "The configured WorkItems selection query has no WHERE clause, so it cannot bound the TFS export. " +
                    "Add a WHERE clause, or remove the query to export the whole project.");
            var start = whereAt + "WHERE".Length;
            if (FindTopLevelKeyword(q, "ASOF", start) >= 0)
                throw new InvalidOperationException(
                    "The configured WorkItems selection query uses ASOF, which a TFS export cannot apply: the export " +
                    "selects work items in date windows. Remove ASOF from the query.");
            var orderAt = FindTopLevelKeyword(q, "ORDER", start);
            where = (orderAt >= 0 ? q.Substring(start, orderAt - start) : q.Substring(start)).Trim();
        }

        return $"SELECT [System.Id] FROM WorkItems WHERE ({ResolveProjectMacro(where, project)})";
    }

    /// <summary>Index of <paramref name="keyword"/> as a whole word outside quotes and brackets, or -1.</summary>
    private static int FindTopLevelKeyword(string query, string keyword, int from)
    {
        var inQuote = false;
        var inBracket = false;
        for (var i = from; i < query.Length; i++)
        {
            var c = query[i];
            if (inQuote)
            {
                if (c == '\'')
                {
                    if (i + 1 < query.Length && query[i + 1] == '\'') i++; // escaped ''
                    else inQuote = false;
                }
                continue;
            }
            if (inBracket)
            {
                if (c == ']') inBracket = false;
                continue;
            }
            if (c == '\'') { inQuote = true; continue; }
            if (c == '[') { inBracket = true; continue; }
            if (i + keyword.Length <= query.Length
                && string.Compare(query, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (i == 0 || !IsWordChar(query[i - 1]))
                && (i + keyword.Length == query.Length || !IsWordChar(query[i + keyword.Length])))
                return i;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '@';

    /// <summary>Replaces the <c>@project</c> macro outside quoted literals only.</summary>
    private static string ResolveProjectMacro(string where, string project)
    {
        var result = new System.Text.StringBuilder(where.Length + project.Length);
        var at = 0;
        while (true)
        {
            var next = FindTopLevelKeyword(where, "@project", at);
            if (next < 0)
            {
                result.Append(where, at, where.Length - at);
                return result.ToString();
            }
            result.Append(where, at, next - at).Append('\'').Append(EscapeWiql(project)).Append('\'');
            at = next + "@project".Length;
        }
    }

    private sealed class LoggingProgressSink : IProgressSink
    {
        private readonly ILogger _logger;

        public LoggingProgressSink(ILogger logger) => _logger = logger;

        public void Emit(ProgressEvent evt)
        {
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogInformation("[{Module}/{Stage}] {Message}", evt.Module, evt.Stage, evt.Message);
        }
    }
}
