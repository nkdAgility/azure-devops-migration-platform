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
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using PlatformWorkItemField = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// Seeds and resolves target work items by querying a configured provenance field on TeamFoundationServer targets.
/// </summary>
public sealed class TfsTargetFieldResolutionStrategy : IWorkItemResolutionStrategy
{
    private static readonly Regex FieldNamePattern = new(@"^[A-Za-z0-9_.-]+$", RegexOptions.Compiled);
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly IWorkItemTarget _target;
    private readonly string _project;
    private readonly string _fieldName;

    public TfsTargetFieldResolutionStrategy(
        WorkItemTrackingHttpClient witClient,
        IWorkItemTarget target,
        string project,
        string fieldName)
    {
        _witClient = witClient ?? throw new ArgumentNullException(nameof(witClient));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
    }

    /// <inheritdoc/>
    public async Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct)
    {
        EnsureValidFieldName();
        var safeProject = EscapeWiqlLiteral(_project);
        var wiql = new Wiql
        {
            Query = $"SELECT [System.Id], [{_fieldName}] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{safeProject}' " +
                    $"AND [{_fieldName}] <> '' " +
                    $"ORDER BY [System.Id]"
        };

        var result = await _witClient
            .QueryByWiqlAsync(wiql, _project, top: null, cancellationToken: ct)
            .ConfigureAwait(false);

        if (result.WorkItems is null || !result.WorkItems.Any())
            return;

        var ids = result.WorkItems.Select(r => r.Id).ToList();
        const int batchSize = 200;

        async IAsyncEnumerable<IdMapEntry> GetMappings()
        {
            for (int i = 0; i < ids.Count; i += batchSize)
            {
                var batch = ids.Skip(i).Take(batchSize).ToList();
                var workItems = await _witClient.GetWorkItemsAsync(
                    batch,
                    fields: new[] { "System.Id", _fieldName },
                    cancellationToken: ct).ConfigureAwait(false);

                foreach (var wi in workItems)
                {
                    if (wi.Fields is null)
                        continue;
                    if (!wi.Fields.TryGetValue(_fieldName, out var rawSourceId))
                        continue;
                    if (!int.TryParse(rawSourceId?.ToString(), out var sourceId))
                        continue;
                    var targetId = wi.Id ?? 0;
                    if (sourceId > 0 && targetId > 0)
                        yield return new IdMapEntry { SourceId = sourceId, TargetId = targetId };
                }
            }
        }

        await idMapStore.SeedWorkItemMappingsAsync(GetMappings(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct)
    {
        EnsureValidFieldName();
        var safeProject = EscapeWiqlLiteral(_project);
        var wiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{safeProject}' " +
                    $"AND [{_fieldName}] = '{sourceWorkItemId}'"
        };

        var result = await _witClient
            .QueryByWiqlAsync(wiql, _project, top: 1, cancellationToken: ct)
            .ConfigureAwait(false);

        return result.WorkItems?.FirstOrDefault()?.Id;
    }

    /// <inheritdoc/>
    public async Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct)
    {
        var fields = new List<PlatformWorkItemField>
        {
            new() { ReferenceName = _fieldName, Value = sourceWorkItemId.ToString() }
        };

        await _target.UpdateFieldsAsync(targetWorkItemId, fields, ct).ConfigureAwait(false);
    }

    private void EnsureValidFieldName()
    {
        if (!FieldNamePattern.IsMatch(_fieldName))
            throw new InvalidOperationException($"Invalid field name '{_fieldName}' for WIQL.");
    }

    private static string EscapeWiqlLiteral(string value) => value.Replace("'", "''");
}
