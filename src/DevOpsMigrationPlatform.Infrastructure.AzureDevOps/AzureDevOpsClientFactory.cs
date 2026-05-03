// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Default implementation of <see cref="IAzureDevOpsClientFactory"/>.
/// All <see cref="VssConnection"/> and credential construction is centralised here.
/// </summary>
internal sealed class AzureDevOpsClientFactory : IAzureDevOpsClientFactory
{
    public Task<ProjectHttpClient> CreateProjectClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(endpoint);
        return connection.GetClientAsync<ProjectHttpClient>(cancellationToken);
    }

    public Task<WorkItemTrackingHttpClient> CreateWorkItemClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(endpoint);
        return connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);
    }

    public Task<GitHttpClient> CreateGitClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(endpoint);
        return connection.GetClientAsync<GitHttpClient>(cancellationToken);
    }

    public Task<TeamHttpClient> CreateTeamClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(endpoint);
        return connection.GetClientAsync<TeamHttpClient>(cancellationToken);
    }

    public Task<WorkHttpClient> CreateWorkClientAsync(
        OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(endpoint);
        return connection.GetClientAsync<WorkHttpClient>(cancellationToken);
    }

    private static VssConnection CreateConnection(OrganisationEndpoint endpoint)
    {
        var pat = endpoint.Authentication.ResolvedAccessToken;
        var credentials = string.IsNullOrEmpty(pat)
            ? new VssCredentials()
            : (VssCredentials)new VssBasicCredential(string.Empty, pat);
        return new VssConnection(new Uri(endpoint.ResolvedUrl), credentials);
    }
}
