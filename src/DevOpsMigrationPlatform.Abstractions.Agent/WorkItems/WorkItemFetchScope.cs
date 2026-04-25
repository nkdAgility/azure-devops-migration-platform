using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Encapsulates query-scope concerns for
/// <see cref="DevOpsMigrationPlatform.Abstractions.Services.IWorkItemFetchService.FetchAsync"/>:
/// which fields to project, which filters to apply, and an optional base WIQL WHERE clause.
/// </summary>
/// <param name="Fields">
/// Field reference names to project (e.g. <c>"System.WorkItemType"</c>, <c>"System.State"</c>).
/// Must not be null or empty.
/// </param>
/// <param name="FilterOptions">
/// Optional in-process filter predicates. <see langword="null"/> or empty = all items pass.
/// </param>
/// <param name="BaseQuery">
/// Optional WIQL WHERE clause fragment appended to the window strategy's query.
/// <see langword="null"/> = no additional constraint.
/// </param>
/// <param name="ResumeEnabled">
/// When <see langword="true"/>, the fetch service attempts to resume from
/// <paramref name="SavedContinuationToken"/>. Default: <see langword="false"/>.
/// </param>
/// <param name="SavedContinuationToken">
/// Continuation token from a prior run. Only inspected when <paramref name="ResumeEnabled"/> is true.
/// </param>
/// <param name="ContinuationCheckpointWriter">
/// Callback invoked per-batch with the latest checkpoint. If null and ResumeEnabled is true,
/// a warning log is emitted and checkpoints are silently skipped.
/// </param>
public sealed record WorkItemFetchScope(
    IReadOnlyList<string> Fields,
    IReadOnlyList<WorkItemFieldFilterOptions>? FilterOptions = null,
    string? BaseQuery = null,
    bool ResumeEnabled = false,
    BatchContinuationToken? SavedContinuationToken = null,
    Func<BatchContinuationToken, CancellationToken, Task>? ContinuationCheckpointWriter = null);
