// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;
using Comment = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.Comment;
using CommentList = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.CommentList;
using CommentVersion = Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.CommentVersion;
using WorkItemComment = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemComment;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Comments;

/// <summary>
/// Reads work item comments, including edited and deleted versions, from the comments REST API
/// that TFS 2019+ and Azure DevOps serve (#171, folded into #169). Comments added in a revision
/// already travel as <c>System.History</c>; this source supplies the edits and deletes the
/// Comments extension exports. Streams one page at a time.
/// </summary>
internal sealed class TfsWorkItemCommentSource : IWorkItemCommentSource
{
    /// <summary>Fetches one page: work item ID, continuation token, include deleted.</summary>
    internal delegate Task<CommentList> FetchPage(int workItemId, string? continuationToken, bool includeDeleted, CancellationToken ct);

    /// <summary>Fetches every version of one comment: work item ID, comment ID.</summary>
    internal delegate Task<IReadOnlyList<CommentVersion>> FetchVersions(int workItemId, int commentId, CancellationToken ct);

    private readonly FetchPage _fetchPage;
    private readonly FetchVersions? _fetchVersions;
    private readonly ILogger<TfsWorkItemCommentSource> _logger;

    internal TfsWorkItemCommentSource(FetchPage fetchPage, ILogger<TfsWorkItemCommentSource> logger, FetchVersions? fetchVersions = null)
    {
        _fetchPage = fetchPage ?? throw new ArgumentNullException(nameof(fetchPage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fetchVersions = fetchVersions;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            CommentList page;
            try
            {
                page = await _fetchPage(workItemId, continuationToken, includeDeleted, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "[TFS Comments] Could not read comments for work item {WorkItemId}.", workItemId);
                throw;
            }

            if (page?.Comments == null || !page.Comments.Any())
                yield break;

            foreach (var comment in page.Comments)
            {
                // The list holds only the latest version. An edited comment is read with its
                // history so every edit reaches the revision it belongs to (PR #222 review).
                if (comment.Version > 1 && _fetchVersions is not null)
                {
                    var versions = await _fetchVersions(workItemId, comment.Id, cancellationToken).ConfigureAwait(false);
                    foreach (var version in versions.OrderBy(v => v.Version))
                        yield return Map(version, comment);
                    continue;
                }
                yield return Map(comment);
            }

            continuationToken = page.ContinuationToken;
            if (string.IsNullOrEmpty(continuationToken))
                yield break;
        }
    }

    private static WorkItemComment Map(Comment comment) => new()
    {
        CommentId = comment.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Version = comment.Version,
        Text = comment.Text ?? string.Empty,
        RenderedText = comment.RenderedText,
        Format = comment.Format.ToString().ToLowerInvariant(),
        IsDeleted = comment.IsDeleted,
        CreatedBy = Identity(comment.CreatedBy),
        CreatedDate = new DateTimeOffset(DateTime.SpecifyKind(comment.CreatedDate, DateTimeKind.Utc)),
        ModifiedBy = Identity(comment.ModifiedBy),
        ModifiedDate = new DateTimeOffset(DateTime.SpecifyKind(comment.ModifiedDate, DateTimeKind.Utc)),
    };

    private static WorkItemComment Map(CommentVersion version, Comment latest) => new()
    {
        CommentId = version.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Version = version.Version,
        Text = version.Text ?? string.Empty,
        RenderedText = version.RenderedText,
        Format = latest.Format.ToString().ToLowerInvariant(),
        IsDeleted = version.IsDeleted,
        CreatedBy = Identity(version.CreatedBy ?? latest.CreatedBy),
        CreatedDate = new DateTimeOffset(DateTime.SpecifyKind(version.CreatedDate == default ? latest.CreatedDate : version.CreatedDate, DateTimeKind.Utc)),
        ModifiedBy = Identity(version.ModifiedBy),
        ModifiedDate = new DateTimeOffset(DateTime.SpecifyKind(version.ModifiedDate, DateTimeKind.Utc)),
    };

    private static WorkItemIdentityRef Identity(Microsoft.VisualStudio.Services.WebApi.IdentityRef? identity) => new()
    {
        DisplayName = identity?.DisplayName ?? "Unknown",
        UniqueName = identity?.UniqueName ?? "unknown",
        Descriptor = identity?.Descriptor.ToString() ?? string.Empty,
    };
}
