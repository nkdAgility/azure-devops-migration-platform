using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="ITeamSource"/>.
/// Uses the Azure DevOps Teams REST API to enumerate teams and their data.
/// Note: Full endpoint context (org URL, PAT) is required but not available
/// via this interface. This implementation logs a warning and returns empty data.
/// A future iteration should extend ITeamSource to accept endpoint context.
/// </summary>
public sealed class AzureDevOpsTeamSource : ITeamSource
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsTeamSource> _logger;

    public AzureDevOpsTeamSource(
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsTeamSource> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogWarning(
            "[Teams/ADO] AzureDevOpsTeamSource: full team enumeration requires OrganisationEndpoint context. " +
            "This implementation returns no teams. Extend ITeamSource in a future iteration.");
        yield break;
    }

    /// <inheritdoc/>
    public Task<TeamSettings?> GetTeamSettingsAsync(
        string projectName, string teamId, CancellationToken ct)
        => Task.FromResult<TeamSettings?>(null);

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield break;
    }

    /// <inheritdoc/>
    public Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        string projectName, string teamId, string iterationId, CancellationToken ct)
        => Task.FromResult(Array.Empty<TeamCapacityEntry>());

    /// <inheritdoc/>
    public Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        string projectName, string teamId, CancellationToken ct)
        => Task.FromResult<TeamAreaPaths?>(null);
}
