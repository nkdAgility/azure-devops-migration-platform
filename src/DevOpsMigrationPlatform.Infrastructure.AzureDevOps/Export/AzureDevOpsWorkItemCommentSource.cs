using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;

/// <summary>
/// Fetches work item comments from the Azure DevOps REST API using the Comments API v7.1-preview.4.
/// Implements streaming enumeration with pagination support for memory safety on large comment volumes.
/// </summary>
public class AzureDevOpsWorkItemCommentSource : IWorkItemCommentSource
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly OrganisationEndpoint _endpoint;
    private readonly string _project;
    private readonly ILogger<AzureDevOpsWorkItemCommentSource> _logger;
    private const int PageSize = 200; // ADO API max page size for comments

    public AzureDevOpsWorkItemCommentSource(
        IAzureDevOpsClientFactory clientFactory,
        OrganisationEndpoint endpoint,
        string project,
        ILogger<AzureDevOpsWorkItemCommentSource> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _project = project ?? throw new ArgumentNullException(nameof(project));
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
        // Create the client on-demand from the factory.
        var client = await _clientFactory.CreateWorkItemClientAsync(_endpoint, cancellationToken);

        // Use the project-scoped overload: GetCommentsAsync(project, workItemId, top, continuationToken, ...)
        // This returns CommentList with a ContinuationToken property for safe server-side pagination.
        // The overload without 'project' resolves to the revision-history API; its positional parameters
        // are (id, fromRevision, top, ...), so PageSize lands in fromRevision and skip=0 lands in $top,
        // causing the server to reject $top=0 with "outside permissible range".
        string? continuationToken = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.CommentList? response = null;
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

            // CommentList.ContinuationToken is null when there are no more pages.
            continuationToken = response.ContinuationToken;
            if (string.IsNullOrEmpty(continuationToken))
                yield break;
        }
    }

    /// <summary>
    /// Maps an ADO SDK <see cref="Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.Comment"/>
    /// (returned by the project-scoped CommentsAPI) to the platform <see cref="WorkItemComment"/> record.
    /// </summary>
    private static WorkItemComment MapCommentFromAdoSdk(
        Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.Comment adoComment)
    {
        var createdBy = new WorkItemIdentityRef
        {
            DisplayName = adoComment.CreatedBy?.DisplayName ?? "Unknown",
            UniqueName = adoComment.CreatedBy?.UniqueName ?? "unknown",
            Descriptor = adoComment.CreatedBy?.Descriptor ?? string.Empty,
        };

        var modifiedBy = new WorkItemIdentityRef
        {
            DisplayName = adoComment.ModifiedBy?.DisplayName ?? "Unknown",
            UniqueName = adoComment.ModifiedBy?.UniqueName ?? "unknown",
            Descriptor = adoComment.ModifiedBy?.Descriptor ?? string.Empty,
        };

        return new WorkItemComment
        {
            CommentId = adoComment.Id.ToString(),
            Version = adoComment.Version,
            Text = adoComment.Text ?? string.Empty,
            RenderedText = adoComment.RenderedText,
            Format = adoComment.Format.ToString().ToLowerInvariant(),
            IsDeleted = adoComment.IsDeleted,
            CreatedBy = createdBy,
            CreatedDate = new DateTimeOffset(adoComment.CreatedDate),
            ModifiedBy = modifiedBy,
            ModifiedDate = new DateTimeOffset(adoComment.ModifiedDate),
        };
    }
}
