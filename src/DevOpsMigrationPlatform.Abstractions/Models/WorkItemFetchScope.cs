using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Models;

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
public sealed record WorkItemFetchScope(
    IReadOnlyList<string> Fields,
    IReadOnlyList<WorkItemFieldFilterOptions>? FilterOptions = null,
    string? BaseQuery = null);
