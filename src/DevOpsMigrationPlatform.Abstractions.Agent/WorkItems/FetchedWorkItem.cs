using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Represents a single work item with only the requested field values.
/// Yielded one-at-a-time from
/// <see cref="DevOpsMigrationPlatform.Abstractions.Services.IWorkItemFetchService.FetchAsync"/>.
/// </summary>
/// <param name="Id">The work item ID in the source system.</param>
/// <param name="Fields">Fetched field values keyed by reference name. Missing fields are omitted.</param>
public sealed record FetchedWorkItem(
    int Id,
    IReadOnlyDictionary<string, object?> Fields);
