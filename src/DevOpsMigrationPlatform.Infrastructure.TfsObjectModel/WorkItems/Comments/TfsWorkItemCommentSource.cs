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

    /// <summary>
    /// Whether a collection serves the comments API, shared by every source for that collection:
    /// once it is found missing, later revisions skip without another request (Martin,
    /// 2026-11-18: build the TFS agent as if there may be no REST API).
    /// </summary>
    internal sealed class ApiAvailability
    {
        private volatile bool _missing;
        public bool Missing => _missing;
        public void MarkMissing() => _missing = true;
    }

    private readonly FetchPage _fetchPage;
    private readonly FetchVersions? _fetchVersions;
    private readonly ILogger<TfsWorkItemCommentSource> _logger;
    private readonly ApiAvailability _availability;

    internal TfsWorkItemCommentSource(FetchPage fetchPage, ILogger<TfsWorkItemCommentSource> logger, FetchVersions? fetchVersions = null, ApiAvailability? availability = null)
    {
        _fetchPage = fetchPage ?? throw new ArgumentNullException(nameof(fetchPage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fetchVersions = fetchVersions;
        _availability = availability ?? new ApiAvailability();
    }

    private static CommentsApiUnavailableException Unavailable(Exception? inner = null) => new(
        "This TFS server does not serve the work item comments API (Azure DevOps Server 2019 Update 1 or later). " +
        "Comments are exported from each revision's History; there are no comment edits to export.", inner);

    /// <inheritdoc/>
    public async IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_availability.Missing)
            throw Unavailable();

        string? continuationToken = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            CommentList page;
            try
            {
                page = await _fetchPage(workItemId, continuationToken, includeDeleted, cancellationToken).ConfigureAwait(false);
            }
            // TFS 2015-2018 and Azure DevOps Server 2019 RTM have no 5.1 comments API, and no
            // comment edits: their comments are System.History, already exported with each
            // revision (Martin, 2026-11-18: TFS 2015 and later). Report that, so the extension
            // skips; any other failure fails the revision.
            catch (Exception ex) when (IsCommentsApiMissing(ex))
            {
                _availability.MarkMissing();
                throw Unavailable(ex);
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

    /// <summary>
    /// True when the server positively lacks the comments resource or the API version range:
    /// the SDK negotiates down to a server's highest supported version, so these mean the
    /// resource itself is not served.
    /// </summary>
    internal static bool IsCommentsApiMissing(Exception ex) =>
        ex is Microsoft.VisualStudio.Services.WebApi.VssResourceNotFoundException
           or Microsoft.VisualStudio.Services.WebApi.VssVersionOutOfRangeException
        || ex is Microsoft.VisualStudio.Services.WebApi.VssServiceResponseException { HttpStatusCode: System.Net.HttpStatusCode.NotFound }
        // TFS 2008 has no REST API at all: a plain 404 (Martin, 2026-11-18). On net481
        // HttpRequestException carries no status code, so the standard message is matched too.
        || ex is System.Net.Http.HttpRequestException http
           && (http.Data["StatusCode"] is System.Net.HttpStatusCode.NotFound
               || http.Message.IndexOf("404 (Not Found)", StringComparison.OrdinalIgnoreCase) >= 0);

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
