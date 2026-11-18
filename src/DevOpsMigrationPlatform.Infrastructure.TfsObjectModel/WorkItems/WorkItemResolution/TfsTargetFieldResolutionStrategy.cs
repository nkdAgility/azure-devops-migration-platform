// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using PlatformWorkItemField = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;

/// <summary>
/// Seeds and resolves target work items by querying a configured provenance field on
/// TeamFoundationServer targets. Queries run through the Object Model
/// (<see cref="ITfsWorkItemQuery"/>), not REST, so TFS 2008 targets work too (Martin, 2026-11-18).
/// </summary>
public sealed class TfsTargetFieldResolutionStrategy : IWorkItemResolutionStrategy
{
    private static readonly Regex FieldNamePattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly ITfsWorkItemQuery _query;
    private readonly IWorkItemTarget _target;
    private readonly string _project;
    private readonly string _fieldName;

    public TfsTargetFieldResolutionStrategy(
        ITfsWorkItemQuery query,
        IWorkItemTarget target,
        string project,
        string fieldName)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    /// <inheritdoc/>
    public async Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct)
    {
        EnsureValidFieldName();
        var wiql = $"SELECT [System.Id], [{_fieldName}] FROM WorkItems " +
                   $"WHERE [System.TeamProject] = '{EscapeWiqlLiteral(_project)}' " +
                   $"AND [{_fieldName}] <> '' " +
                   $"ORDER BY [System.Id]";

        // Streams the Object Model's paged result one item at a time.
        async IAsyncEnumerable<IdMapEntry> GetMappings()
        {
            foreach (var (targetId, rawSourceId) in _query.Query(wiql, _fieldName))
            {
                ct.ThrowIfCancellationRequested();
                if (int.TryParse(rawSourceId, out var sourceId) && sourceId > 0 && targetId > 0)
                    yield return new IdMapEntry { SourceId = sourceId, TargetId = targetId };
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        await idMapStore.SeedWorkItemMappingsAsync(GetMappings(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct)
    {
        EnsureValidFieldName();
        var wiql = $"SELECT [System.Id] FROM WorkItems " +
                   $"WHERE [System.TeamProject] = '{EscapeWiqlLiteral(_project)}' " +
                   $"AND [{_fieldName}] = '{sourceWorkItemId}'";

        foreach (var (targetId, _) in _query.Query(wiql, _fieldName))
            return Task.FromResult<int?>(targetId);
        return Task.FromResult<int?>(null);
    }

    /// <inheritdoc/>
    public ImportProvenance ProvenanceFor(int sourceWorkItemId) => new(
        new List<PlatformWorkItemField> { new() { ReferenceName = _fieldName, Value = sourceWorkItemId.ToString() } },
        new List<HyperlinkWorkItemLink>());

    private void EnsureValidFieldName()
    {
        if (!FieldNamePattern.IsMatch(_fieldName))
            throw new InvalidOperationException($"Invalid field name '{_fieldName}' for WIQL.");
    }

    private static string EscapeWiqlLiteral(string value) => value.Replace("'", "''");
}