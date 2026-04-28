using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="ITeamTarget"/>.
/// Note: Full endpoint context is required but not available via this interface.
/// This implementation logs a warning and no-ops all writes.
/// A future iteration should extend ITeamTarget to accept endpoint context.
/// </summary>
public sealed class AzureDevOpsTeamTarget : ITeamTarget
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsTeamTarget> _logger;

    public AzureDevOpsTeamTarget(
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsTeamTarget> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<string> CreateOrUpdateTeamAsync(
        string projectName, TeamDefinition team, CancellationToken ct)
    {
        _logger.LogWarning(
            "[Teams/ADO] AzureDevOpsTeamTarget: CreateOrUpdateTeamAsync requires OrganisationEndpoint context. " +
            "This implementation is a stub. Extend ITeamTarget in a future iteration.");
        return Task.FromResult(team.Id);
    }

    /// <inheritdoc/>
    public Task SetTeamSettingsAsync(
        string projectName, string teamId, TeamSettings settings, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task AssignIterationAsync(
        string projectName, string teamId, TeamIteration iteration, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task AddMemberAsync(
        string projectName, string teamId, TeamMember member, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task SetCapacityAsync(
        string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task SetAreaPathsAsync(
        string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct)
        => Task.CompletedTask;
}
