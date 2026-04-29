using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="IIdentitySource"/>.
/// Enumerates project team members by listing all teams and deduplicating by UniqueName.
/// </summary>
internal sealed class AzureDevOpsIdentitySource : IIdentitySource
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsIdentitySource> _logger;

    internal AzureDevOpsIdentitySource(
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsIdentitySource> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        MigrationEndpointOptions endpoint,
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("[Identities/ADO] Enumerating identities for project '{Project}'.", projectName);

        var org = endpoint.ToOrganisationEndpoint();
        var teamClient = await _clientFactory.CreateTeamClientAsync(org, cancellationToken).ConfigureAwait(false);

        var teams = await teamClient.GetTeamsAsync(projectName, cancellationToken: cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("[Identities/ADO] Found {Count} teams in project '{Project}'.", teams.Count, projectName);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var team in teams)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Microsoft.VisualStudio.Services.WebApi.TeamMember> members;
            try
            {
                members = await teamClient.GetTeamMembersWithExtendedPropertiesAsync(
                    projectName, team.Id.ToString(), cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Identities/ADO] Failed to get members for team '{Team}' — skipping.", team.Name);
                continue;
            }

            foreach (var member in members)
            {
                var uniqueName = member.Identity?.UniqueName;
                if (string.IsNullOrEmpty(uniqueName) || !seen.Add(uniqueName))
                    continue;

                yield return new IdentityDescriptor(
                    Descriptor: member.Identity?.UniqueName ?? member.Identity?.Id.ToString() ?? uniqueName,
                    DisplayName: member.Identity?.DisplayName ?? uniqueName,
                    UniqueName: uniqueName,
                    SourceType: "User",
                    Origin: "AzureDevOps",
                    IsActive: true);
            }
        }

        _logger.LogInformation("[Identities/ADO] Enumerated {Count} unique identities for project '{Project}'.", seen.Count, projectName);
    }
}
