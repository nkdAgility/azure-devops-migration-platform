using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.Extensions.Logging;
using WorkContext = Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="ITeamTarget"/>.
/// Uses the TeamHttpClient and WorkHttpClient from the Azure DevOps SDK.
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
    public async Task<string> CreateOrUpdateTeamAsync(
        MigrationEndpointOptions endpoint, string projectName, TeamDefinition team, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var teamClient = await _clientFactory.CreateTeamClientAsync(org, ct).ConfigureAwait(false);

        // FR-016: If the source team is the default team, map it to the target project's default team
        // regardless of name differences.
        if (team.IsDefault)
        {
            try
            {
                var allTeams = await teamClient.GetTeamsAsync(projectName, cancellationToken: ct).ConfigureAwait(false);
                // ADO returns the default team as the first entry in the list, or has a ProjectSettings default.
                // Find the default team by checking name or using the first team as fallback.
                var defaultTeam = allTeams.FirstOrDefault()
                    ?? throw new InvalidOperationException($"No teams found in target project '{projectName}' to map default team.");

                var updated = await teamClient.UpdateTeamAsync(
                    new WebApiTeam { Name = defaultTeam.Name, Description = team.Description },
                    projectName, defaultTeam.Id.ToString(), cancellationToken: ct).ConfigureAwait(false);
                _logger.LogInformation("[Teams/ADO] Mapped source default team '{SourceName}' → target default team '{TargetName}' (id={Id}).",
                    team.Name, defaultTeam.Name, updated.Id);
                return updated.Id.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Teams/ADO] Could not resolve default team in project '{Project}' — falling back to name-based lookup.", projectName);
            }
        }

        try
        {
            var existing = await teamClient.GetTeamAsync(projectName, team.Name, cancellationToken: ct).ConfigureAwait(false);
            if (existing is not null)
            {
                var updated = await teamClient.UpdateTeamAsync(
                    new WebApiTeam { Name = team.Name, Description = team.Description },
                    projectName, existing.Id.ToString(), cancellationToken: ct).ConfigureAwait(false);
                _logger.LogDebug("[Teams/ADO] Updated existing team '{Name}' with id '{Id}'.", team.Name, updated.Id);
                return updated.Id.ToString();
            }
        }
        catch (Exception)
        {
            // Team not found — fall through to create
        }

        var created = await teamClient.CreateTeamAsync(
            new WebApiTeam { Name = team.Name, Description = team.Description },
            projectName, cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation("[Teams/ADO] Created team '{Name}' with id '{Id}'.", team.Name, created.Id);
        return created.Id.ToString();
    }

    /// <inheritdoc/>
    public async Task SetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamSettings settings, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var patch = new Microsoft.TeamFoundation.Work.WebApi.TeamSettingsPatch
            {
                BugsBehavior = settings.BugsBehavior
                    ? Microsoft.TeamFoundation.Work.WebApi.BugsBehavior.AsRequirements
                    : Microsoft.TeamFoundation.Work.WebApi.BugsBehavior.Off
            };
            await workClient.UpdateTeamSettingsAsync(patch, teamContext, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to set team settings for team '{TeamId}'.", teamId);
        }
    }

    /// <inheritdoc/>
    public async Task AssignIterationAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamIteration iteration, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var patch = new Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration { Path = iteration.Path };
            await workClient.PostTeamIterationAsync(patch, teamContext, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to assign iteration '{Path}' to team '{TeamId}'.", iteration.Path, teamId);
        }
    }

    /// <inheritdoc/>
    public Task AddMemberAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamMember member, CancellationToken ct)
    {
        // Adding team members via REST API requires the entitlements API (user entitlement management),
        // which is a separate endpoint and requires additional permissions. Log a warning and skip.
        _logger.LogWarning(
            "[Teams/ADO] AddMemberAsync: adding team members via REST requires the Entitlement API. " +
            "Member '{Member}' for team '{TeamId}' was not added. " +
            "Use the Azure DevOps UI or Entitlement API to manage team membership.",
            member.DisplayName, teamId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task SetCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var patches = new List<Microsoft.TeamFoundation.Work.WebApi.TeamMemberCapacityIdentityRef>();
            foreach (var entry in capacity)
            {
                var activities = new List<Microsoft.TeamFoundation.Work.WebApi.Activity>();
                foreach (var act in entry.Activities)
                    activities.Add(new Microsoft.TeamFoundation.Work.WebApi.Activity { Name = act.Name, CapacityPerDay = (float)act.CapacityPerDay });

                patches.Add(new Microsoft.TeamFoundation.Work.WebApi.TeamMemberCapacityIdentityRef
                {
                    TeamMember = new Microsoft.VisualStudio.Services.WebApi.IdentityRef { UniqueName = entry.MemberDescriptor },
                    Activities = activities
                });
            }

            await workClient.ReplaceCapacitiesWithIdentityRefAsync(patches, teamContext, new Guid(iterationId), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to set capacity for team '{TeamId}' iteration '{IterationId}'.", teamId, iterationId);
        }
    }

    /// <inheritdoc/>
    public async Task SetAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var values = new List<Microsoft.TeamFoundation.Work.WebApi.TeamFieldValue>
            {
                new() { Value = areaPaths.DefaultAreaPath, IncludeChildren = true }
            };
            foreach (var path in areaPaths.IncludedAreaPaths)
                if (!string.Equals(path, areaPaths.DefaultAreaPath, StringComparison.OrdinalIgnoreCase))
                    values.Add(new Microsoft.TeamFoundation.Work.WebApi.TeamFieldValue { Value = path, IncludeChildren = true });

            var patch = new Microsoft.TeamFoundation.Work.WebApi.TeamFieldValuesPatch
            {
                DefaultValue = areaPaths.DefaultAreaPath,
                Values = values
            };
            await workClient.UpdateTeamFieldValuesAsync(patch, teamContext, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to set area paths for team '{TeamId}'.", teamId);
        }
    }
}
