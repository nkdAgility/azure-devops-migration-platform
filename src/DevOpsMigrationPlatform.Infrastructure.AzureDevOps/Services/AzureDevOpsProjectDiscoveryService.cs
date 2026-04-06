using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Discovers team projects via the Azure DevOps REST API.
/// </summary>
public sealed class AzureDevOpsProjectDiscoveryService : IProjectDiscoveryService
{
    public async Task<List<string>> GetProjectsAsync(
        string orgOrCollection,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var projectClient = await connection.GetClientAsync<ProjectHttpClient>(cancellationToken);
        var projects = await projectClient.GetProjects();
        return projects.Select(p => p.Name).ToList();
    }
}
