// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Seeds the ID map by querying the target for work items with hyperlinks that match a
/// configured URL pattern. The source work item ID is extracted from the matching hyperlink URL.
/// No live per-item fallback is performed (FR-022 compliance).
/// </summary>
internal sealed class TargetHyperlinkResolutionStrategy : IWorkItemResolutionStrategy
{
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly IWorkItemImportTarget _target;
    private readonly string _project;
    private readonly string _urlPattern;
    private readonly Regex? _urlRegex;

    /// <param name="witClient">Azure DevOps WIT HTTP client.</param>
    /// <param name="target">Target for writing provenance hyperlinks.</param>
    /// <param name="project">Team project name.</param>
    /// <param name="urlPattern">
    /// URL pattern containing <c>{id}</c> as the source ID placeholder.
    /// Example: <c>https://source.example.com/wi/{id}</c>
    /// </param>
    public TargetHyperlinkResolutionStrategy(
        WorkItemTrackingHttpClient witClient,
        IWorkItemImportTarget target,
        string project,
        string urlPattern)
    {
        _witClient = witClient ?? throw new ArgumentNullException(nameof(witClient));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _urlPattern = urlPattern ?? throw new ArgumentNullException(nameof(urlPattern));
        // Build a regex from the pattern: replace {id} with a named capture group.
        // When urlPattern is empty no seeding or provenance writing is performed.
        if (!string.IsNullOrEmpty(urlPattern))
        {
            var escapedPattern = Regex.Escape(urlPattern).Replace(@"\{id\}", @"(?<id>\d+)");
            _urlRegex = new Regex(escapedPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }

    /// <inheritdoc/>
    public async Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct)
    {
        if (_urlRegex is null)
            return;
        var wiql = new Wiql
        {
            Query = $"SELECT [System.Id] FROM WorkItems " +
                    $"WHERE [System.TeamProject] = '{_project}' " +
                    $"AND [System.HyperLinkCount] > 0 " +
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
                    expand: WorkItemExpand.Relations,
                    cancellationToken: ct).ConfigureAwait(false);

                foreach (var wi in workItems)
                {
                    if (wi.Relations is null) continue;
                    foreach (var rel in wi.Relations)
                    {
                        if (!string.Equals(rel.Rel, "Hyperlink", StringComparison.OrdinalIgnoreCase)) continue;
                        if (rel.Url is null) continue;
                        var match = _urlRegex.Match(rel.Url);
                        if (!match.Success) continue;
                        if (!int.TryParse(match.Groups["id"].Value, out var sourceId)) continue;
                        var targetId = wi.Id ?? 0;
                        if (sourceId > 0 && targetId > 0)
                            yield return new IdMapEntry { SourceId = sourceId, TargetId = targetId };
                    }
                }
            }
        }

        await idMapStore.SeedWorkItemMappingsAsync(GetMappings(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<int?> ResolveSingleAsync(int sourceId, CancellationToken ct)
    {
        // FR-022: No live per-item lookup for TargetHyperlink — cannot filter by URL content via WIQL.
        return Task.FromResult<int?>(null);
    }

    /// <inheritdoc/>
    public async Task WriteProvenanceAsync(int sourceId, int targetId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_urlPattern))
            return;
        var provenanceUrl = _urlPattern.Replace("{id}", sourceId.ToString(), StringComparison.OrdinalIgnoreCase);
        var hyperlinks = new List<HyperlinkWorkItemLink>
        {
            new() { ArtifactLinkType = "Hyperlink", Location = provenanceUrl }
        };
        await _target.AddLinksAsync(
            targetId,
            relatedLinks: Array.Empty<RelatedWorkItemLink>(),
            externalLinks: Array.Empty<ExternalWorkItemLink>(),
            hyperlinks: hyperlinks,
            ct).ConfigureAwait(false);
    }
}
