// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;

/// <summary>
/// Implements work item link analysis for Azure DevOps Services.
/// Uses <see cref="IWorkItemFetchService"/> for field-projected pre-filtering,
/// then fetches Relations only for items that pass the filter.
/// </summary>
internal sealed class AzureDevOpsDependencyAnalysisService : IWorkItemLinkAnalysisService
{
    private readonly IOptions<MigrationOptions> _options;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly IWorkItemFetchService _fetchService;
    private readonly IWorkItemDiscoveryService _discoveryService;
    private readonly ILogger<AzureDevOpsDependencyAnalysisService> _logger;

    public AzureDevOpsDependencyAnalysisService(
        IOptions<MigrationOptions> options,
        IAzureDevOpsClientFactory clientFactory,
        IWorkItemFetchService fetchService,
        IWorkItemDiscoveryService discoveryService,
        ILogger<AzureDevOpsDependencyAnalysisService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _fetchService = fetchService ?? throw new ArgumentNullException(nameof(fetchService));
        _discoveryService = discoveryService ?? throw new ArgumentNullException(nameof(discoveryService));
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
        MigrationEndpointOptions endpoint,
        string project,
        string? wiqlFilter = null,
        BatchContinuationToken? savedContinuationToken = null,
        Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // All data in this method references org URLs, project names, and WI IDs — customer data.
        var _dataScope = DataClassificationScope.Begin(DataClassification.Customer);
        try
        {
            var orgEndpoint = endpoint.ToOrganisationEndpoint();
            var witClient = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Enumerating work item IDs for project {Project} in {OrgUrl}", project, orgEndpoint.ResolvedUrl);

            // ── Pre-count: discover total work items so progress shows N of M ────
            int projectTotal = 0;
            await foreach (var snapshot in _discoveryService.CountWorkItemsAsync(
                orgEndpoint, project, wiqlFilter, cancellationToken).ConfigureAwait(false))
            {
                projectTotal = snapshot.WorkItemsCount;
            }
            _logger.LogInformation(
                "Project {Project} has {TotalWorkItems} work items to analyse for dependencies.",
                project, projectTotal);

            // Emit initial heartbeat with the discovered total
            yield return new DependencyHeartbeatEvent(
                orgEndpoint.ResolvedUrl, project, 0, 0, 0, 0, false,
                TotalWorkItems: projectTotal, IsCounting: true);

            var sourceOrgSegment = ExtractOrgSegment(orgEndpoint.ResolvedUrl);
            var counters = new LinkCounters();
            const int batchSize = 200;

            // Build a lookup of org-segment → (resolvedUrl, pat) for all configured orgs so we
            // can resolve GUID project names in cross-org links when we have credentials.
            // Disabled organisations are intentionally included here: `enabled: false` only
            // prevents an organisation from being iterated for dependency discovery, but it
            // must still participate in GUID-to-project-name resolution so that links
            // pointing at a disabled org are resolved to human-readable names.
            var configuredOrgs = _options.Value.Organisations
                .OfType<Abstractions.Options.AzureDevOpsOrganisationEntry>()
                .Where(o => !string.IsNullOrWhiteSpace(o.ResolvedUrl))
                .ToDictionary(
                    o => ExtractOrgSegment(o.ResolvedUrl),
                    o => (OrgUrl: o.ResolvedUrl.TrimEnd('/'), Pat: o.Authentication?.ResolvedAccessToken ?? ""),
                    StringComparer.OrdinalIgnoreCase);

            // Shared cache: "orgSegment::guidString" → resolved project name.
            // Prevents redundant API calls when the same GUID appears in multiple links.
            var projectNameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // ── Stream pre-filtered items via IWorkItemFetchService ──────────────
            // Items are streamed with field projection; only items passing any
            // configured filters are yielded. IDs are buffered into batches of 200
            // for the Relations expansion call that follows.
            var scope = new WorkItemFetchScope(
                Fields: new[] { "System.WorkItemType", "System.TeamProject" },
                BaseQuery: string.IsNullOrWhiteSpace(wiqlFilter) ? null : wiqlFilter,
                ResumeEnabled: savedContinuationToken is not null,
                SavedContinuationToken: savedContinuationToken,
                ContinuationCheckpointWriter: continuationCheckpointWriter);

            _logger.LogInformation("Streaming work items for dependency analysis in {Project} at {OrgUrl}", project, orgEndpoint.ResolvedUrl);

            var currentBatch = new List<int>(batchSize);
            int totalStreamed = 0;

            await foreach (var item in _fetchService.FetchAsync(orgEndpoint, project, scope, cancellationToken)
                .ConfigureAwait(false))
            {
                currentBatch.Add(item.Id);
                totalStreamed++;

                if (currentBatch.Count >= batchSize)
                {
                    // Emit counting heartbeat before processing the batch
                    yield return new DependencyHeartbeatEvent(
                        orgEndpoint.ResolvedUrl, project, counters.Processed,
                        counters.CrossProject + counters.CrossOrg,
                        counters.CrossProject, counters.CrossOrg, false,
                        TotalWorkItems: projectTotal, IsCounting: true);

                    await foreach (var evt in ProcessBatchAsync(
                        witClient, currentBatch, sourceOrgSegment, orgEndpoint.ResolvedUrl, project,
                        counters, projectTotal, configuredOrgs, projectNameCache, cancellationToken))
                    {
                        yield return evt;
                    }
                    currentBatch.Clear();
                }
            }

            // Process remaining items in the last partial batch
            if (currentBatch.Count > 0)
            {
                await foreach (var evt in ProcessBatchAsync(
                    witClient, currentBatch, sourceOrgSegment, orgEndpoint.ResolvedUrl, project,
                    counters, projectTotal, configuredOrgs, projectNameCache, cancellationToken))
                {
                    yield return evt;
                }
            }

            // Final heartbeat
            yield return new DependencyHeartbeatEvent(
                orgEndpoint.ResolvedUrl,
                project,
                counters.Processed,
                counters.CrossProject + counters.CrossOrg,
                counters.CrossProject,
                counters.CrossOrg,
                true,
                TotalWorkItems: projectTotal,
                SkippedWorkItems: counters.Skipped);

            _logger.LogInformation(
                "Dependency analysis completed for {Project}: {Processed} work items, {CrossProject} cross-project, {CrossOrg} cross-org",
                project, counters.Processed, counters.CrossProject, counters.CrossOrg);
        }
        finally
        {
            _dataScope.Dispose();
        }
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
        LinkCounters counters,
        int totalWorkItems,
        IReadOnlyDictionary<string, (string OrgUrl, string Pat)> configuredOrgs,
        Dictionary<string, string> projectNameCache,
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

            var sourceStateCategory = workItem.Fields.TryGetValue("System.StateCategory", out var stateCatObj)
                ? stateCatObj?.ToString() ?? ""
                : "";

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
                        // Use credentials presence to determine reachability — no extra HTTP call needed.
                        // Calling with the source access token always yields AccessDenied against a foreign org.
                        var targetStatus = configuredOrgs.ContainsKey(targetOrgSegment)
                            ? TargetStatus.Reachable
                            : TargetStatus.Unknown;
                        var rawProject = ExtractProjectSegment(targetUrl);
                        var resolvedProject = await ResolveTargetProjectAsync(
                            targetOrgSegment, rawProject, configuredOrgs, projectNameCache, cancellationToken).ConfigureAwait(false);
                        eventToYield = new DependencyFoundEvent(new DependencyRecord
                        {
                            SourceWorkItemId = sourceId,
                            SourceWorkItemType = sourceType,
                            SourceProject = sourceProject,
                            SourceOrganisationUrl = organisationUrl,
                            LinkType = relation.Rel.Replace("System.LinkTypes.", ""),
                            LinkScope = LinkScope.CrossOrganisation,
                            TargetWorkItemId = targetId,
                            TargetProject = resolvedProject,
                            TargetOrganisation = targetOrgSegment,
                            TargetStatus = targetStatus,
                            LinkChangedDate = ExtractLinkChangedDate(relation),
                            SourceWorkItemStateCategory = sourceStateCategory
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
                                TargetStatus = TargetStatus.Reachable,
                                LinkChangedDate = ExtractLinkChangedDate(relation),
                                SourceWorkItemStateCategory = sourceStateCategory
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
    /// If <paramref name="rawProject"/> looks like a GUID and the target org is configured
    /// with credentials, resolves the GUID to the actual project name via the Core API.
    /// Results are cached in <paramref name="cache"/> to avoid repeat calls.
    /// Falls back to <paramref name="rawProject"/> on any error or missing credentials.
    /// </summary>
    private async Task<string> ResolveTargetProjectAsync(
        string targetOrgSegment,
        string rawProject,
        IReadOnlyDictionary<string, (string OrgUrl, string Pat)> configuredOrgs,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(rawProject, out var projectGuid))
            return rawProject; // already a name

        if (!configuredOrgs.TryGetValue(targetOrgSegment, out var creds))
            return rawProject; // no credentials for this org

        var cacheKey = $"{targetOrgSegment}::{rawProject}";
        if (cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var projectClient = await _clientFactory.CreateProjectClientAsync(
                new OrganisationEndpoint
                {
                    ResolvedUrl = creds.OrgUrl,
                    Authentication = new OrganisationEndpointAuthentication
                    {
                        Type = Abstractions.Options.AuthenticationType.Pat,
                        ResolvedAccessToken = creds.Pat
                    }
                }, cancellationToken).ConfigureAwait(false);
            var teamProject = await projectClient.GetProject(
                projectGuid.ToString()).ConfigureAwait(false);
            var name = teamProject?.Name ?? rawProject;
            cache[cacheKey] = name;
            _logger.LogDebug("Resolved project GUID {Guid} in {Org} to '{Name}'",
                rawProject, targetOrgSegment, name);
            return name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not resolve project GUID {Guid} in {Org}: {Message}",
                rawProject, targetOrgSegment, ex.Message);
            cache[cacheKey] = rawProject; // don't retry
            return rawProject;
        }
    }

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

    /// <summary>
    /// Extracts the <c>changedDate</c> attribute from a work item relation, if present.
    /// The ADO REST API populates this attribute for link-type relations; it represents
    /// when the link was last modified (and equals the creation date when the link is new).
    /// Returns <c>null</c> when the attribute is absent or cannot be parsed.
    /// </summary>
    private static DateTimeOffset? ExtractLinkChangedDate(WorkItemRelation relation)
    {
        if (relation.Attributes == null)
            return null;

        if (!relation.Attributes.TryGetValue("changedDate", out var raw))
            return null;

        return raw switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(dt, TimeSpan.Zero),
            string s when DateTimeOffset.TryParse(s, out var parsed) => parsed,
            _ => null
        };
    }

}
