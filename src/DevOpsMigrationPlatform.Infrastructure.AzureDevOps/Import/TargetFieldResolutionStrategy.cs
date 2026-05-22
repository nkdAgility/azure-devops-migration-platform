// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

using PlatformWorkItemField = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Seeds the ID map by querying the target for work items that already have a custom
/// provenance field populated (e.g. <c>Custom.SourceWorkItemId</c>).
/// During processing, if a source ID is not in <c>idmap.db</c>, a single WIQL query
/// is performed as a live fallback. After creation, the provenance field is written.
/// </summary>
internal sealed class TargetFieldResolutionStrategy : IWorkItemResolutionStrategy
{
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly IWorkItemImportTarget _target;
    private readonly string _project;
    private readonly string _fieldName;
    private readonly ILogger<TargetFieldResolutionStrategy> _logger;

    public TargetFieldResolutionStrategy(
        WorkItemTrackingHttpClient witClient,
        IWorkItemImportTarget target,
        string project,
        string fieldName,
        ILogger<TargetFieldResolutionStrategy> logger)
    {
        _witClient = witClient ?? throw new ArgumentNullException(nameof(witClient));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _fieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct)
    {
        var wiql = new Wiql
        {
            Query = $"SELECT [System.Id], [{_fieldName}] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{_project}' " +
                    $"AND [{_fieldName}] <> '' " +
                    $"ORDER BY [System.Id]"
        };

        Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItemQueryResult result;
        try
        {
            result = await _witClient
                .QueryByWiqlAsync(wiql, _project, top: null, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (VssServiceException ex) when (ex.Message.Contains("TF51005"))
        {
            _logger.LogError(
                "[WorkItems] Field '{FieldName}' does not exist in project '{Project}' (TF51005). " +
                "Work item writes requiring this field will also fail. " +
                "Create the field in the target project or correct the FieldName in WorkItemResolutionStrategy config.",
                _fieldName, _project);
            throw new MigrationException(
                $"Field '{_fieldName}' does not exist in project '{_project}' (TF51005). " +
                "Create the field in the target project or correct the FieldName in WorkItemResolutionStrategy config.",
                MigrationErrorCategory.ValidationError,
                isRetryable: false,
                guidance: $"Add field '{_fieldName}' to project '{_project}' via Project Settings > Work Items > Fields, then re-run the import.",
                innerException: ex);
        }

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
                    if (wi.Fields is null) continue;
                    if (!wi.Fields.TryGetValue(_fieldName, out var rawSourceId)) continue;
                    if (!int.TryParse(rawSourceId?.ToString(), out var sourceId)) continue;
                    var targetId = wi.Id ?? 0;
                    if (sourceId > 0 && targetId > 0)
                        yield return new IdMapEntry { SourceId = sourceId, TargetId = targetId };
                }
            }
        }

        await idMapStore.SeedWorkItemMappingsAsync(GetMappings(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int?> ResolveSingleAsync(int sourceId, CancellationToken ct)
    {
        var wiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{_project}' " +
                    $"AND [{_fieldName}] = '{sourceId}'"
        };

        var result = await _witClient
            .QueryByWiqlAsync(wiql, _project, top: 1, cancellationToken: ct)
            .ConfigureAwait(false);

        return result.WorkItems?.FirstOrDefault()?.Id;
    }

    /// <inheritdoc/>
    public async Task WriteProvenanceAsync(int sourceId, int targetId, CancellationToken ct)
    {
        var fields = new List<PlatformWorkItemField>
        {
            new() { ReferenceName = _fieldName, Value = sourceId.ToString() }
        };
        await _target.UpdateFieldsAsync(targetId, fields, ct).ConfigureAwait(false);
    }
}
