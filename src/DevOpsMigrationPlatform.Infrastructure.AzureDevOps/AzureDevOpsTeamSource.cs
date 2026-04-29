using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using WorkContext = Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="ITeamSource"/>.
/// Uses the TeamHttpClient and WorkHttpClient from the Azure DevOps SDK.
/// </summary>
internal sealed class AzureDevOpsTeamSource : ITeamSource
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
        MigrationEndpointOptions endpoint,
        string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var teamClient = await _clientFactory.CreateTeamClientAsync(org, ct).ConfigureAwait(false);
        var teams = await teamClient.GetTeamsAsync(projectName, cancellationToken: ct).ConfigureAwait(false);

        foreach (var team in teams)
        {
            ct.ThrowIfCancellationRequested();
            yield return new TeamDefinition(
                Id: team.Id.ToString(),
                Name: team.Name,
                Description: team.Description ?? string.Empty,
                IsDefault: false);
        }
    }

    /// <inheritdoc/>
    public async Task<TeamSettings?> GetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var settings = await workClient.GetTeamSettingsAsync(teamContext, cancellationToken: ct).ConfigureAwait(false);
            return new TeamSettings(
                BacklogNavigationLevel: settings.BacklogIteration?.Name ?? string.Empty,
                BugsBehavior: settings.BugsBehavior == Microsoft.TeamFoundation.Work.WebApi.BugsBehavior.AsRequirements,
                WorkingDays: MapWorkingDays(settings.WorkingDays));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to get team settings for team '{TeamId}' — returning null.", teamId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        List<Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration> iterations;
        try
        {
            iterations = await workClient.GetTeamIterationsAsync(teamContext, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to get iterations for team '{TeamId}'.", teamId);
            yield break;
        }

        foreach (var it in iterations)
        {
            ct.ThrowIfCancellationRequested();
            yield return new TeamIteration(
                Id: it.Id.ToString(),
                Path: it.Path,
                Name: it.Name,
                StartDate: it.Attributes?.StartDate.HasValue == true ? new DateTimeOffset(it.Attributes.StartDate.Value) : null,
                FinishDate: it.Attributes?.FinishDate.HasValue == true ? new DateTimeOffset(it.Attributes.FinishDate.Value) : null,
                IsDefault: false,
                IsBacklog: false);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var teamClient = await _clientFactory.CreateTeamClientAsync(org, ct).ConfigureAwait(false);

        List<Microsoft.VisualStudio.Services.WebApi.TeamMember> members;
        try
        {
            members = await teamClient.GetTeamMembersWithExtendedPropertiesAsync(
                projectName, teamId, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to get members for team '{TeamId}'.", teamId);
            yield break;
        }

        foreach (var m in members)
        {
            ct.ThrowIfCancellationRequested();
            var uniqueName = m.Identity?.UniqueName ?? string.Empty;
            yield return new TeamMember(
                Descriptor: m.Identity?.UniqueName ?? m.Identity?.Id.ToString() ?? uniqueName,
                DisplayName: m.Identity?.DisplayName ?? uniqueName,
                UniqueName: uniqueName,
                IsAdmin: m.IsTeamAdmin);
        }
    }

    /// <inheritdoc/>
    public async Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var capacities = await workClient.GetCapacitiesWithIdentityRefAsync(
                teamContext, new Guid(iterationId), cancellationToken: ct).ConfigureAwait(false);

            var result = new List<TeamCapacityEntry>();
            foreach (var cap in capacities)
            {
                var activities = new List<ActivityEntry>();
                if (cap.Activities is not null)
                {
                    foreach (var act in cap.Activities)
                        activities.Add(new ActivityEntry(act.Name ?? string.Empty, act.CapacityPerDay));
                }

                result.Add(new TeamCapacityEntry(
                    MemberDescriptor: cap.TeamMember?.UniqueName ?? string.Empty,
                    MemberDisplayName: cap.TeamMember?.DisplayName ?? string.Empty,
                    Activities: activities.ToArray(),
                    DaysOff: cap.DaysOff == null ? 0 : cap.DaysOff.Count()));
            }
            return result.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to get capacity for team '{TeamId}' iteration '{IterationId}'.", teamId, iterationId);
            return Array.Empty<TeamCapacityEntry>();
        }
    }

    /// <inheritdoc/>
    public async Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
    {
        var org = endpoint.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(projectName, teamId);

        try
        {
            var fieldValues = await workClient.GetTeamFieldValuesAsync(teamContext, cancellationToken: ct).ConfigureAwait(false);
            var defaultPath = fieldValues.DefaultValue ?? string.Empty;
            var included = new List<string>();
            if (fieldValues.Values is not null)
            {
                foreach (var v in fieldValues.Values)
                    if (!string.IsNullOrEmpty(v.Value))
                        included.Add(v.Value);
            }
            return new TeamAreaPaths(defaultPath, included);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams/ADO] Failed to get area paths for team '{TeamId}'.", teamId);
            return null;
        }
    }

    private static string[] MapWorkingDays(System.Collections.Generic.IEnumerable<System.DayOfWeek>? days)
    {
        if (days is null) return Array.Empty<string>();
        var result = new System.Collections.Generic.List<string>();
        foreach (var d in days)
            result.Add(d.ToString());
        return result.ToArray();
    }
}
