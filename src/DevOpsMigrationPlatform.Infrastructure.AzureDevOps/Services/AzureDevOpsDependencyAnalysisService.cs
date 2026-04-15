using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Implements work item link analysis for Azure DevOps Services.
/// Uses <see cref="IWorkItemQueryWindowStrategy"/> for work item ID enumeration (handles 20K
/// WIQL limit) then fetches relations in batches via the REST API.
/// </summary>
public sealed class AzureDevOpsDependencyAnalysisService : IWorkItemLinkAnalysisService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly ILogger<AzureDevOpsDependencyAnalysisService> _logger;

    public AzureDevOpsDependencyAnalysisService(
        IOptions<DiscoveryOptions> options,
        IAzureDevOpsClientFactory clientFactory,
        IWorkItemQueryWindowStrategy windowStrategy,
        ILogger<AzureDevOpsDependencyAnalysisService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyses all work item links in an Azure DevOps project.
    /// Uses <see cref="IWorkItemQueryWindowStrategy"/> to enumerate IDs (handles 20K WIQL cap),
    /// then fetches each work item with relations expanded and classifies cross-project and
    /// cross-organisation links.
    /// Results are streamed as DependencyFoundEvent and DependencyHeartbeatEvent records.
    /// </summary>
    public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
        string organisationUrl,
        string project,
        string pat,
        string? wiqlFilter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var witClient = await _clientFactory.CreateWorkItemClientAsync(organisationUrl, pat, cancellationToken).ConfigureAwait(false);

        var windowOptions = string.IsNullOrWhiteSpace(wiqlFilter)
            ? null
            : new WorkItemQueryWindowOptions { BaseQuery = wiqlFilter };

        _logger.LogInformation("Enumerating work item IDs for project {Project} in {OrgUrl}", project, organisationUrl);

        var sourceOrgSegment = ExtractOrgSegment(organisationUrl);
        var counters = new LinkCounters();
        const int batchSize = 200;

        // ── Phase 1: collect all work item IDs so we know the total up-front ──
        // IDs are plain integers; even 200 K IDs occupy ~800 KB — well within budget.
        _logger.LogInformation("Counting work items for project {Project} in {OrgUrl}", project, organisationUrl);
        var allIds = new List<int>();
        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            organisationUrl, project, pat, windowOptions, cancellationToken).ConfigureAwait(false))
        {
            allIds.AddRange(window.WorkItemIds);
        }

        var totalWorkItems = allIds.Count;
        _logger.LogInformation("Found {Total} work items to analyse in {Project}", totalWorkItems, project);

        // Emit an initial heartbeat so the CLI can display the total immediately.
        yield return new DependencyHeartbeatEvent(
            organisationUrl, project, 0, 0, 0, 0, false,
            TotalWorkItems: totalWorkItems);

        // ── Phase 2: process IDs in batches ───────────────────────────────────
        for (var offset = 0; offset < allIds.Count; offset += batchSize)
        {
            var batch = allIds.GetRange(offset, Math.Min(batchSize, allIds.Count - offset));

            await foreach (var evt in ProcessBatchAsync(
                witClient, batch, sourceOrgSegment, organisationUrl, project, pat,
                counters, totalWorkItems, cancellationToken))
            {
                yield return evt;
            }
        }

        // Final heartbeat
        yield return new DependencyHeartbeatEvent(
            organisationUrl,
            project,
            counters.Processed,
            counters.CrossProject + counters.CrossOrg,
            counters.CrossProject,
            counters.CrossOrg,
            true,
            TotalWorkItems: totalWorkItems,
            SkippedWorkItems: counters.Skipped);

        _logger.LogInformation(
            "Dependency analysis completed for {Project}: {Processed} work items, {CrossProject} cross-project, {CrossOrg} cross-org",
            project, counters.Processed, counters.CrossProject, counters.CrossOrg);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed class LinkCounters
    {
        public int CrossProject { get; set; }
        public int CrossOrg { get; set; }
        public int Processed { get; set; }
        public int Skipped { get; set; }
    }

    private async IAsyncEnumerable<DependencyProgressEvent> ProcessBatchAsync(
        WorkItemTrackingHttpClient witClient,
        IReadOnlyList<int> batchIds,
        string sourceOrgSegment,
        string organisationUrl,
        string project,
        string pat,
        LinkCounters counters,
        int totalWorkItems,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("Fetching batch of {BatchSize} work items", batchIds.Count);

        var (workItems, skippedInBatch) = await FetchWorkItemsAsync(
            witClient, batchIds, cancellationToken).ConfigureAwait(false);
        counters.Skipped += skippedInBatch;

        foreach (var workItem in workItems)
        {
            if (workItem.Relations == null || workItem.Relations.Count == 0)
            {
                counters.Processed++;
                continue;
            }

            var sourceProject = workItem.Fields.TryGetValue("System.TeamProject", out var projObj)
                ? projObj.ToString() ?? "Unknown"
                : "Unknown";

            var sourceId = workItem.Id ?? 0;
            if (sourceId == 0)
            {
                counters.Processed++;
                continue;
            }

            var sourceType = workItem.Fields.TryGetValue("System.WorkItemType", out var typeObj)
                ? typeObj.ToString() ?? "Unknown"
                : "Unknown";

            foreach (var relation in workItem.Relations.Where(r =>
                !string.IsNullOrEmpty(r.Rel) && r.Rel.StartsWith("System.LinkTypes.")))
            {
                DependencyProgressEvent? eventToYield = null;

                try
                {
                    var targetUrl = relation.Url;

                    // ADO REST work-item URLs use camelCase: .../wit/workItems/{id}
                    // Use case-insensitive index to locate the segment, then extract the ID.
                    var wiIdx = targetUrl.IndexOf("/workitems/", StringComparison.OrdinalIgnoreCase);
                    if (wiIdx < 0)
                        continue;

                    var idSegment = targetUrl.Substring(wiIdx + "/workitems/".Length);
                    var qMark = idSegment.IndexOf('?');
                    if (qMark >= 0)
                        idSegment = idSegment.Substring(0, qMark);

                    if (!int.TryParse(idSegment, out var targetId))
                        continue;

                    // Cross-org: compare the organisation segment of source and target URLs.
                    // All ADO orgs share dev.azure.com as the host — the org name is the
                    // first path segment, so host comparison alone is insufficient.
                    var targetOrgSegment = ExtractOrgSegment(targetUrl);
                    var isCrossOrg = !string.Equals(sourceOrgSegment, targetOrgSegment, StringComparison.OrdinalIgnoreCase);

                    if (isCrossOrg)
                    {
                        counters.CrossOrg++;
                        var targetStatus = await VerifyTargetStatusAsync(targetUrl, pat, cancellationToken).ConfigureAwait(false);
                        eventToYield = new DependencyFoundEvent(new DependencyRecord
                        {
                            SourceWorkItemId = sourceId,
                            SourceWorkItemType = sourceType,
                            SourceProject = sourceProject,
                            SourceOrganisationUrl = organisationUrl,
                            LinkType = relation.Rel.Replace("System.LinkTypes.", ""),
                            LinkScope = LinkScope.CrossOrganisation,
                            TargetWorkItemId = targetId,
                            TargetProject = ExtractProjectSegment(targetUrl),
                            TargetOrganisation = targetOrgSegment,
                            TargetStatus = targetStatus
                        });
                    }
                    else
                    {
                        // Same org — check if same project
                        var targetProjectName = await GetProjectNameAsync(witClient, targetId, cancellationToken).ConfigureAwait(false);
                        if (!string.Equals(targetProjectName, sourceProject, StringComparison.OrdinalIgnoreCase))
                        {
                            counters.CrossProject++;
                            eventToYield = new DependencyFoundEvent(new DependencyRecord
                            {
                                SourceWorkItemId = sourceId,
                                SourceWorkItemType = sourceType,
                                SourceProject = sourceProject,
                                SourceOrganisationUrl = organisationUrl,
                                LinkType = relation.Rel.Replace("System.LinkTypes.", ""),
                                LinkScope = LinkScope.CrossProject,
                                TargetWorkItemId = targetId,
                                TargetProject = targetProjectName,
                                TargetOrganisation = "",
                                TargetStatus = TargetStatus.Reachable
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing relation for work item {WorkItemId}", sourceId);
                }

                if (eventToYield != null)
                    yield return eventToYield;
            }

            counters.Processed++;
        }

        // Emit heartbeat after each batch
        yield return new DependencyHeartbeatEvent(
            organisationUrl,
            project,
            counters.Processed,
            counters.CrossProject + counters.CrossOrg,
            counters.CrossProject,
            counters.CrossOrg,
            false,
            TotalWorkItems: totalWorkItems,
            SkippedWorkItems: counters.Skipped);
    }

    /// <summary>
    /// Attempts to fetch a batch of work items in one call.
    /// If any item in the batch is inaccessible (TF401232) that call fails for the
    /// whole batch, so we fall back to fetching each ID individually, silently
    /// skipping and counting the ones the caller cannot read.
    /// </summary>
    private async Task<(IReadOnlyList<WorkItem> Items, int Skipped)> FetchWorkItemsAsync(
        WorkItemTrackingHttpClient witClient,
        IReadOnlyList<int> batchIds,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await witClient.GetWorkItemsAsync(
                batchIds,
                expand: WorkItemExpand.Relations,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return (items, 0);
        }
        catch (Exception ex) when (IsPermissionError(ex))
        {
            _logger.LogWarning(
                "Batch of {Count} work items contains inaccessible item(s); falling back to individual fetches",
                batchIds.Count);

            var items = new List<WorkItem>(batchIds.Count);
            var skipped = 0;

            foreach (var id in batchIds)
            {
                try
                {
                    var item = await witClient.GetWorkItemAsync(
                        id,
                        expand: WorkItemExpand.Relations,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    items.Add(item);
                }
                catch (Exception itemEx) when (IsPermissionError(itemEx))
                {
                    _logger.LogWarning(
                        "Work item {WorkItemId} skipped — insufficient permissions: {Message}",
                        id, itemEx.Message);
                    skipped++;
                }
            }

            return (items, skipped);
        }
    }

    /// Returns true when the exception signals that a work item cannot be read
    /// due to it not existing or the caller lacking read permissions (TF401232).
    private static bool IsPermissionError(Exception ex) =>
        ex.Message.Contains("TF401232", StringComparison.Ordinal);

    /// <summary>
    /// Extracts the organisation identifier from an Azure DevOps URL.
    /// For dev.azure.com URLs the first path segment is the org name.
    /// For legacy visualstudio.com URLs the subdomain is the org name.
    /// Falls back to the host if no segment can be determined.
    /// </summary>
    private static string ExtractOrgSegment(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        // dev.azure.com/{org}/... — org is first non-empty path segment
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : uri.Host;
        }

        // {org}.visualstudio.com — org is the subdomain
        var hostParts = uri.Host.Split('.');
        if (hostParts.Length >= 3 && hostParts[^2].Equals("visualstudio", StringComparison.OrdinalIgnoreCase))
            return hostParts[0];

        return uri.Host;
    }

    /// <summary>
    /// Extracts the project name from an Azure DevOps work-item URL.
    /// dev.azure.com/{org}/{project}/_apis/wit/workItems/{id} → project (segment index 1)
    /// {org}.visualstudio.com/{project}/_apis/... → project (segment index 0)
    /// Returns an empty string if the project cannot be determined.
    /// </summary>
    private static string ExtractProjectSegment(string url)
    {
        if (string.IsNullOrEmpty(url))
            return string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.Empty;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // dev.azure.com/{org}/{project}/...
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 1 ? segments[1] : string.Empty;

        // {org}.visualstudio.com/{project}/...
        var hostParts = uri.Host.Split('.');
        if (hostParts.Length >= 3 && hostParts[^2].Equals("visualstudio", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 0 ? segments[0] : string.Empty;

        return string.Empty;
    }

    private static async Task<string> GetProjectNameAsync(
        WorkItemTrackingHttpClient client,
        int workItemId,
        CancellationToken cancellationToken)
    {
        try
        {
            var workItem = await client.GetWorkItemAsync(workItemId, cancellationToken: cancellationToken).ConfigureAwait(false);
            return workItem.Fields.TryGetValue("System.TeamProject", out var projObj)
                ? projObj.ToString() ?? "Unknown"
                : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static async Task<TargetStatus> VerifyTargetStatusAsync(
        string targetUrl,
        string pat,
        CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            if (!string.IsNullOrEmpty(pat))
            {
                var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
            }

            var response = await httpClient.SendAsync(
                new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, targetUrl),
                cancellationToken).ConfigureAwait(false);

            return response.IsSuccessStatusCode ? TargetStatus.Reachable : TargetStatus.Deleted;
        }
        catch
        {
            return TargetStatus.Unknown;
        }
    }
}
