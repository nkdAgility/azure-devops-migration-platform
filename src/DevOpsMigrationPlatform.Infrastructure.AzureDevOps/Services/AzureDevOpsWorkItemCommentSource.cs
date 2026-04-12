using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Fetches work item comments from the Azure DevOps REST API using the Comments API v7.1-preview.4.
/// Implements streaming enumeration with pagination support for memory safety on large comment volumes.
/// </summary>
public class AzureDevOpsWorkItemCommentSource : IWorkItemCommentSource
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly string _organisationUrl;
    private readonly string _project;
    private readonly string _pat;
    private readonly ILogger<AzureDevOpsWorkItemCommentSource> _logger;
    private const int PageSize = 200; // ADO API max page size for comments

    public AzureDevOpsWorkItemCommentSource(
        IAzureDevOpsClientFactory clientFactory,
        string organisationUrl,
        string project,
        string pat,
        ILogger<AzureDevOpsWorkItemCommentSource> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _organisationUrl = organisationUrl ?? throw new ArgumentNullException(nameof(organisationUrl));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _pat = pat ?? throw new ArgumentNullException(nameof(pat));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Fetches all comments for a work item from the ADO Comments API using pagination.
    /// Streams results to avoid loading entire result set into memory.
    /// </summary>
    public async IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Create the client on-demand from the factory (this is async, so we need to await it once before the loop)
        var client = await _clientFactory.CreateWorkItemClientAsync(_organisationUrl, _pat, cancellationToken);

        // Use the project-scoped overload: GetCommentsAsync(project, workItemId, top, continuationToken, ...)
        // The overload without 'project' resolves to the revision-history API, not the comments API,
        // causing positional mismatches that send $top=0 → "outside permissible range" server error.
        string? continuationToken = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItemComments? response = null;
            try
            {
                response = await client.GetCommentsAsync(
                    project: _project,
                    workItemId: workItemId,
                    top: PageSize,
                    continuationToken: continuationToken,
                    includeDeleted: includeDeleted ? (bool?)true : null,
                    expand: null,
                    order: null,
                    userState: null,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching comments for work item {WorkItemId} with continuationToken={ContinuationToken}",
                    workItemId, continuationToken ?? "null");
                throw;
            }

            if (response?.Comments == null || !response.Comments.Any())
                yield break;

            foreach (var adoComment in response.Comments)
                yield return MapCommentFromAdoSdk(adoComment);

            // Advance to the next page using the continuation token from _links.next.
            // The WorkItemComments model does not expose ContinuationToken as a property;
            // it is encoded in the hypermedia "next" link instead.
            continuationToken = ExtractContinuationToken(response.Links);
            if (string.IsNullOrEmpty(continuationToken))
                yield break;
        }
    }

    /// <summary>
    /// Parses the <c>continuationToken</c> query parameter from the <c>_links.next</c> href
    /// returned by the ADO Comments API, or returns <c>null</c> if no next page exists.
    /// </summary>
    private static string? ExtractContinuationToken(ReferenceLinks? links)
    {
        if (links?.Links == null || !links.Links.TryGetValue("next", out var nextObj))
            return null;

        var href = nextObj is ReferenceLink rl ? rl.Href : nextObj?.ToString();
        if (string.IsNullOrEmpty(href))
            return null;

        // Parse continuationToken from the next-page query string.
        var query = new Uri(href).Query.TrimStart('?');
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 &&
                parts[0].Equals("continuationToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    /// <summary>
    /// Maps an ADO SDK WorkItemComment to the platform WorkItemComment record.
    /// </summary>
    private static WorkItemComment MapCommentFromAdoSdk(
        Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItemComment adoComment)
    {
        // Extract identity info from RevisedBy
        var revisedBy = adoComment.RevisedBy;
        var revisedDate = adoComment.RevisedDate;
        if (revisedDate == System.DateTime.MinValue)
        {
            revisedDate = System.DateTime.UtcNow;
        }
        var revisedDateOffset = new DateTimeOffset(revisedDate);

        var identity = new WorkItemIdentityRef
        {
            DisplayName = revisedBy?.DisplayName ?? "Unknown",
            UniqueName = revisedBy?.UniqueName ?? "unknown",
            Descriptor = revisedBy?.Descriptor ?? string.Empty,
        };

        return new WorkItemComment
        {
            CommentId = adoComment.Revision.ToString(), // Use revision as comment ID since API doesn't expose Id
            Version = adoComment.Revision,
            Text = adoComment.Text ?? string.Empty,
            RenderedText = null, // ADO Comments API v7.1 doesn't provide rendered HTML
            Format = "plaintext", // Assume plaintext format
            IsDeleted = false, // ADO doesn't expose deleted flag for comments
            CreatedBy = identity,
            CreatedDate = revisedDateOffset,
            ModifiedBy = identity,
            ModifiedDate = revisedDateOffset,
        };
    }
}
