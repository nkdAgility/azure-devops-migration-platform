using Microsoft.TeamFoundation.Core.WebApi;
using MigrationPlatform.CLI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace MigrationPlatform.CLI.Services
{
    public interface ICatalogService
    {
        IAsyncEnumerable<ProjectDiscoverySummary> CountAllWorkItemsAsync(string orgUrl, string project, string pat, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<TeamProjectReference>> GetProjectsAsync(string orgUrl, string pat);

    }
}
