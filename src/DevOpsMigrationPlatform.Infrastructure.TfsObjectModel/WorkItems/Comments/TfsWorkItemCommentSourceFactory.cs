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
internal sealed class TfsWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory
{
    private const int PageSize = 200;
    private readonly ILoggerFactory _loggerFactory;

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
        var client = new Lazy<WorkItemTrackingHttpClient>(
            () => new VssConnection(new Uri(organisation.ResolvedUrl), credentials).GetClient<WorkItemTrackingHttpClient>());

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
            _loggerFactory.CreateLogger<TfsWorkItemCommentSource>());
    }
}
