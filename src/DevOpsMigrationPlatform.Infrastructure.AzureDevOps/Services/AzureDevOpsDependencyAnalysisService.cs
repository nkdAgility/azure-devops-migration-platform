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
/// Uses the Azure DevOps REST API to fetch work items and analyse their links.
/// </summary>
public sealed class AzureDevOpsDependencyAnalysisService : IWorkItemLinkAnalysisService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsDependencyAnalysisService> _logger;

    public AzureDevOpsDependencyAnalysisService(
        IOptions<DiscoveryOptions> options,
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsDependencyAnalysisService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Analyses all work item links in an Azure DevOps project.
    /// Fetches work items matching the WIQL filter, inspects their links, and classifies them.
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

        // Use provided WIQL or default to all work items
        var wiql = string.IsNullOrWhiteSpace(wiqlFilter)
            ? "SELECT [System.Id] FROM WorkItems"
            : wiqlFilter;

        _logger.LogInformation("Querying work items with WIQL: {Wiql}", wiql);

        // Execute WIQL query
        var queryResult = await witClient.QueryByWiqlAsync(
            new Wiql { Query = wiql },
            project: project,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // De-duplicate work item IDs
        var workItemIds = new HashSet<int>(queryResult.WorkItems.Select(wi => wi.Id));

        _logger.LogInformation("Found {WorkItemCount} work items", workItemIds.Count);

        var crossProjectCount = 0;
        var crossOrgCount = 0;
        var processedCount = 0;
        var batchSize = 200;

        // Process work items in batches with concurrency control
        var workItemIdsList = workItemIds.ToList();

        for (int i = 0; i < workItemIdsList.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batchIds = workItemIdsList.Skip(i).Take(batchSize).ToList();

            _logger.LogDebug("Fetching batch of {BatchSize} work items", batchIds.Count);

            // Fetch work items with relations expanded
            var workItems = await witClient.GetWorkItemsAsync(
                batchIds,
                expand: WorkItemExpand.Relations,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            foreach (var workItem in workItems)
            {
                if (workItem.Relations == null || workItem.Relations.Count == 0)
                {
                    _logger.LogDebug("Work item {WorkItemId} has no relations", workItem.Id);
                    continue;
                }

                var sourceProject = workItem.Fields.TryGetValue("System.TeamProject", out var projObj)
                    ? projObj.ToString() ?? "Unknown"
                    : "Unknown";

                var sourceId = workItem.Id ?? 0;
                if (sourceId == 0)
                    continue;

                var sourceType = workItem.Fields.TryGetValue("System.WorkItemType", out var typeObj)
                    ? typeObj.ToString() ?? "Unknown"
                    : "Unknown";

                // Log relations that don't match filter
                var skippedRelations = workItem.Relations.Where(r => r.Rel != "System.LinkTypes.Related" &&
                                                                      r.Rel != "System.LinkTypes.Dependency-forward" &&
                                                                      r.Rel != "System.LinkTypes.Dependency-reverse" &&
                                                                      !r.Rel.StartsWith("System.LinkTypes."));
                foreach (var skipped in skippedRelations)
                {
                    _logger.LogDebug("Skipping relation of type {RelationType} for work item {WorkItemId}", skipped.Rel, sourceId);
                }

                foreach (var relation in workItem.Relations.Where(r => r.Rel == "System.LinkTypes.Related" ||
                                                                        r.Rel == "System.LinkTypes.Dependency-forward" ||
                                                                        r.Rel == "System.LinkTypes.Dependency-reverse" ||
                                                                        r.Rel.StartsWith("System.LinkTypes.")))
                {
                    _logger.LogDebug("Processing relation of type {RelationType} for work item {WorkItemId}", relation.Rel, sourceId);
                    DependencyProgressEvent? eventToYield = null;

                    try
                    {
                        var targetUrl = relation.Url;
                        if (!targetUrl.Contains("/workitems/"))
                            continue;

                        // Parse target work item ID from URL
                        var urlParts = targetUrl.Split(new[] { "/workitems/" }, StringSplitOptions.None);
                        if (urlParts.Length < 2 || !int.TryParse(urlParts.Last(), out var targetId))
                            continue;

                        // Determine if cross-org or cross-project
                        var targetHost = new Uri(targetUrl).Host;
                        var sourceHost = new Uri(organisationUrl).Host;
                        var isCrossOrg = !targetHost.Equals(sourceHost, StringComparison.OrdinalIgnoreCase);

                        if (isCrossOrg)
                        {
                            crossOrgCount++;
                            var targetStatus = await VerifyTargetStatusAsync(targetUrl, pat, cancellationToken).ConfigureAwait(false);
                            eventToYield = new DependencyFoundEvent(new DependencyRecord
                            {
                                SourceWorkItemId = sourceId,
                                SourceWorkItemType = sourceType,
                                SourceProject = sourceProject,
                                LinkType = relation.Rel.Replace("System.LinkTypes.", ""),
                                LinkScope = LinkScope.CrossOrganisation,
                                TargetWorkItemId = targetId,
                                TargetProject = "",
                                TargetOrganisation = targetHost,
                                TargetStatus = targetStatus
                            });
                        }
                        else
                        {
                            // Same org - check if same project
                            var targetProjectName = await GetProjectNameAsync(witClient, targetId, cancellationToken).ConfigureAwait(false);
                            if (targetProjectName != sourceProject)
                            {
                                crossProjectCount++;
                                eventToYield = new DependencyFoundEvent(new DependencyRecord
                                {
                                    SourceWorkItemId = sourceId,
                                    SourceWorkItemType = sourceType,
                                    SourceProject = sourceProject,
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
                    {
                        yield return eventToYield;
                    }
                }

                processedCount++;
            }

            // Emit heartbeat after each batch
            yield return new DependencyHeartbeatEvent(
                organisationUrl,
                project,
                processedCount,
                crossProjectCount + crossOrgCount,
                crossProjectCount,
                crossOrgCount,
                false);
        }

        // Final heartbeat
        yield return new DependencyHeartbeatEvent(
            organisationUrl,
            project,
            processedCount,
            crossProjectCount + crossOrgCount,
            crossProjectCount,
            crossOrgCount,
            true);
    }

    private async Task<string> GetProjectNameAsync(
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching project name for work item {WorkItemId}", workItemId);
            return "Unknown";
        }
    }

    private async Task<TargetStatus> VerifyTargetStatusAsync(
        string targetUrl,
        string pat,
        CancellationToken cancellationToken)
    {
        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                if (!string.IsNullOrEmpty(pat))
                {
                    var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{pat}"));
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
                }

                var response = await client.SendAsync(
                    new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, targetUrl),
                    cancellationToken).ConfigureAwait(false);

                return response.IsSuccessStatusCode ? TargetStatus.Reachable : TargetStatus.Deleted;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error verifying target status for {TargetUrl}", targetUrl);
            return TargetStatus.Unknown;
        }
    }
}
