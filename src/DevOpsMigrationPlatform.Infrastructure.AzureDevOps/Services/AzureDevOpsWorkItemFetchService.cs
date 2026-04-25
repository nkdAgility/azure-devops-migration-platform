using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemFetchService"/>.
/// Uses <see cref="IWorkItemQueryWindowStrategy"/> for date-windowed WIQL queries
/// and batch-fetches only the declared fields via the REST API.
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates in-process
/// before yielding each item.
/// </summary>
public sealed class AzureDevOpsWorkItemFetchService : IWorkItemFetchService
{
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private const int BatchSize = 200;

    public AzureDevOpsWorkItemFetchService(
        IWorkItemQueryWindowStrategy windowStrategy,
        IAzureDevOpsClientFactory clientFactory)
    {
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (scope.Fields is null || scope.Fields.Count == 0)
            throw new ArgumentException("Fields must not be null or empty.", nameof(scope));

        var witClient = await _clientFactory.CreateWorkItemClientAsync(endpoint, cancellationToken);

        // Build window options, wiring resume fields when enabled.
        var windowOptions = new WorkItemQueryWindowOptions
        {
            BaseQuery = scope.BaseQuery,
            ResumeEnabled = scope.ResumeEnabled,
            SavedContinuationToken = scope.SavedContinuationToken
        };

        int lastYieldedId = 0;
        DateTime lastChangedDate = DateTime.MinValue;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            endpoint, project, windowOptions, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var batch in window.WorkItemIds.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var workItems = await witClient.GetWorkItemsAsync(
                    batch.ToList(),
                    fields: scope.Fields.ToArray(),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var wi in workItems)
                {
                    var fields = wi.Fields.ToDictionary(
                        kvp => kvp.Key,
                        kvp => (object?)kvp.Value);

                    var item = new FetchedWorkItem(
                        wi.Id ?? 0,
                        fields);

                    if (PassesFilters(item, scope.FilterOptions))
                    {
                        lastYieldedId = item.Id;
                        // Track ChangedDate for checkpoint if available
                        if (fields.TryGetValue("System.ChangedDate", out var cd) && cd is DateTime dt)
                            lastChangedDate = dt;
                        else
                            lastChangedDate = window.WindowEnd;

                        yield return item;
                    }
                }

                // Emit per-batch checkpoint when resume is enabled
                if (scope.ResumeEnabled && scope.ContinuationCheckpointWriter is not null && lastYieldedId > 0)
                {
                    var checkpoint = new BatchContinuationToken
                    {
                        StrategyVersion = "1.0",
                        ChangedDateUtc = lastChangedDate.Kind == DateTimeKind.Utc
                            ? lastChangedDate
                            : lastChangedDate.ToUniversalTime(),
                        WorkItemId = lastYieldedId,
                        QueryFingerprint = scope.SavedContinuationToken?.QueryFingerprint ?? string.Empty,
                        GeneratedAtUtc = DateTime.UtcNow,
                        Completed = false
                    };
                    await scope.ContinuationCheckpointWriter(checkpoint, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        // T022: Emit completion checkpoint at end-of-stream
        if (scope.ResumeEnabled && scope.ContinuationCheckpointWriter is not null)
        {
            var completionToken = new BatchContinuationToken
            {
                StrategyVersion = "1.0",
                ChangedDateUtc = lastChangedDate != DateTime.MinValue
                    ? (lastChangedDate.Kind == DateTimeKind.Utc ? lastChangedDate : lastChangedDate.ToUniversalTime())
                    : DateTime.UtcNow,
                WorkItemId = lastYieldedId,
                QueryFingerprint = scope.SavedContinuationToken?.QueryFingerprint ?? string.Empty,
                GeneratedAtUtc = DateTime.UtcNow,
                Completed = true
            };
            await scope.ContinuationCheckpointWriter(completionToken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Delegates to the shared <see cref="WorkItemFieldFilterEvaluator.PassesFilters"/>.
    /// Kept as an internal static for backward compatibility with tests.
    /// </summary>
    internal static bool PassesFilters(
        FetchedWorkItem item,
        IReadOnlyList<WorkItemFieldFilterOptions>? filters) =>
        WorkItemFieldFilterEvaluator.PassesFilters(item, filters);

    /// <inheritdoc />
    public Task<ResumeDecision> EvaluateResumeDecisionAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default)
    {
        if (!scope.ResumeEnabled || scope.SavedContinuationToken is null)
        {
            return Task.FromResult(new ResumeDecision
            {
                Status = ResumeDecisionStatus.Unavailable,
                Reason = "no_saved_token"
            });
        }

        var savedFingerprint = scope.SavedContinuationToken.QueryFingerprint;
        // When the scope carries a current fingerprint via QueryParameters,
        // compare it. Otherwise use the base query text as a simple fingerprint proxy.
        var currentFingerprint = scope.BaseQuery ?? string.Empty;

        // If the caller has pre-computed a fingerprint via IQueryFingerprintService
        // and stored it in the token, compare saved vs current.
        // For now we compare the saved fingerprint against the current query text
        // when no explicit fingerprint is provided. This is consistent with the
        // contract: if the query changes, the fingerprint changes.
        if (!string.IsNullOrEmpty(savedFingerprint) &&
            !string.Equals(savedFingerprint, currentFingerprint, StringComparison.Ordinal))
        {
            return Task.FromResult(new ResumeDecision
            {
                Status = ResumeDecisionStatus.RejectedQueryMismatch,
                Reason = "query_fingerprint_mismatch",
                SavedQueryFingerprint = savedFingerprint,
                CurrentQueryFingerprint = currentFingerprint,
                TokenStrategyVersion = scope.SavedContinuationToken.StrategyVersion
            });
        }

        return Task.FromResult(new ResumeDecision
        {
            Status = ResumeDecisionStatus.Accepted,
            SavedQueryFingerprint = savedFingerprint,
            CurrentQueryFingerprint = currentFingerprint,
            TokenStrategyVersion = scope.SavedContinuationToken.StrategyVersion
        });
    }
}
