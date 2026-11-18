// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Comments;

/// <summary>
/// Creates a <see cref="TfsWorkItemCommentSource"/> for the job's source endpoint, authenticated
/// with its access token, or Windows credentials when there is none (on-premises TFS).
/// </summary>
internal sealed class TfsWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory, IDisposable
{
    private const int PageSize = 200;
    private readonly ILoggerFactory _loggerFactory;
    // One connection per collection, disposed with the factory (a singleton): a source is created
    // for every revision, so a connection per source was never disposed (PR #222 review).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<VssConnection>> _connections = new(StringComparer.OrdinalIgnoreCase);
    // Per collection: once the comments API is found missing it is not asked again (Martin, 2026-11-18).
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TfsWorkItemCommentSource.ApiAvailability> _availability = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
            if (connection.IsValueCreated)
                connection.Value.Dispose();
        _connections.Clear();
    }

    public TfsWorkItemCommentSourceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public IWorkItemCommentSource Create(MigrationEndpointOptions endpoint, string project)
    {
        if (endpoint == null)
            throw new ArgumentNullException(nameof(endpoint));
        if (string.IsNullOrWhiteSpace(project))
            throw new ArgumentException("Project is required.", nameof(project));

        var organisation = endpoint.ToOrganisationEndpoint();
        if (string.IsNullOrWhiteSpace(organisation.ResolvedUrl))
            throw new ArgumentException("Collection URL is required.", nameof(endpoint));

        var token = organisation.Authentication?.ResolvedAccessToken;
        VssCredentials credentials = !string.IsNullOrWhiteSpace(token)
            ? new VssCredentials(new VssBasicCredential(string.Empty, token))
            : new VssClientCredentials(true);
        var connection = _connections.GetOrAdd(organisation.ResolvedUrl,
            url => new Lazy<VssConnection>(() => new VssConnection(new Uri(url), credentials)));
        var client = new Lazy<WorkItemTrackingHttpClient>(() => connection.Value.GetClient<WorkItemTrackingHttpClient>());

        return new TfsWorkItemCommentSource(
            (workItemId, continuationToken, includeDeleted, ct) => client.Value.GetCommentsAsync(
                project: project,
                workItemId: workItemId,
                top: PageSize,
                continuationToken: continuationToken,
                includeDeleted: includeDeleted ? true : (bool?)null,
                expand: null,
                order: null,
                userState: null,
                cancellationToken: ct),
            _loggerFactory.CreateLogger<TfsWorkItemCommentSource>(),
            async (workItemId, commentId, ct) => await client.Value.GetCommentVersionsAsync(
                project: project,
                workItemId: workItemId,
                commentId: commentId,
                cancellationToken: ct).ConfigureAwait(false),
            _availability.GetOrAdd(organisation.ResolvedUrl, _ => new TfsWorkItemCommentSource.ApiAvailability()));
    }
}
