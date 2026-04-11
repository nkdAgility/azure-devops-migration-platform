using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.Extensions.Logging;

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
    private const int PageSize = 100; // ADO API page size for comments

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

        int skip = 0;
        bool hasMoreResults = true;

        while (hasMoreResults && !cancellationToken.IsCancellationRequested)
        {
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItemComments? response = null;
            try
            {
                // Call ADO SDK GetCommentsAsync with pagination
                response = await client.GetCommentsAsync(
                    workItemId,
                    PageSize, // top parameter
                    skip, // $skip parameter (positional, not named)
                    null, // sortOrder
                    null, // userState
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error fetching comments for work item {workItemId} at skip={skip}",
                    workItemId, skip);
                throw;
            }

            if (response?.Comments == null || !response.Comments.Any())
            {
                hasMoreResults = false;
                yield break;
            }

            // Yield each comment
            foreach (var adoComment in response.Comments)
            {
                var comment = MapCommentFromAdoSdk(adoComment);
                yield return comment;
            }

            // Check if there are more results
            if (response.Comments.Count() < PageSize)
            {
                hasMoreResults = false;
            }
            else
            {
                skip += PageSize;
            }
        }
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
