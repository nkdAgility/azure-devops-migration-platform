using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Default implementation of <see cref="IAzureDevOpsClientFactory"/>.
/// All <see cref="VssConnection"/> and credential construction is centralised here.
/// </summary>
public sealed class AzureDevOpsClientFactory : IAzureDevOpsClientFactory
{
    public Task<ProjectHttpClient> CreateProjectClientAsync(
        string url, string pat, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(url, pat);
        return connection.GetClientAsync<ProjectHttpClient>(cancellationToken);
    }

    public Task<WorkItemTrackingHttpClient> CreateWorkItemClientAsync(
        string url, string pat, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(url, pat);
        return connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);
    }

    public Task<GitHttpClient> CreateGitClientAsync(
        string url, string pat, CancellationToken cancellationToken = default)
    {
        var connection = CreateConnection(url, pat);
        return connection.GetClientAsync<GitHttpClient>(cancellationToken);
    }

    private static VssConnection CreateConnection(string url, string pat)
    {
        var credentials = string.IsNullOrEmpty(pat)
            ? new VssCredentials()
            : (VssCredentials)new VssBasicCredential(string.Empty, pat);
        return new VssConnection(new Uri(url), credentials);
    }
}
