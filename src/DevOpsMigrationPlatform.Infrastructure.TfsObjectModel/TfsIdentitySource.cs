// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using AgentIdentityDescriptor = DevOpsMigrationPlatform.Abstractions.Agent.Identity.IdentityDescriptor;
using TfsIdentityDescriptor = Microsoft.TeamFoundation.Framework.Client.IdentityDescriptor;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="IIdentitySource"/>.
/// Uses <see cref="IIdentityManagementService2"/> to enumerate user and group
/// identities in the given project. Export-only; TFS import is not supported.
/// </summary>
public sealed class TfsIdentitySource : IIdentitySource
{
    private readonly TfsTeamProjectCollection _collection;
    private readonly ILogger<TfsIdentitySource> _logger;

    public TfsIdentitySource(
        TfsTeamProjectCollection collection,
        ILogger<TfsIdentitySource> logger)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<AgentIdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Yield control so the method is truly async from the caller's perspective.
        await Task.CompletedTask.ConfigureAwait(false);

        _logger.LogInformation(
            "[Identities][TFS] Enumerating identities for project '{Project}'.", projectName);

        var ims = _collection.GetService<IIdentityManagementService2>();

        // Resolve the project-scope group "[ProjectName]\Project Valid Users".
        // This group transitively contains all users that have been added to the project.
        var projectGroupName = $"[{projectName}]\\Project Valid Users";
        TeamFoundationIdentity[][] projectGroupBatches;
        try
        {
            projectGroupBatches = ims.ReadIdentities(
                IdentitySearchFactor.AccountName,
                new[] { projectGroupName },
                MembershipQuery.Expanded,
                ReadIdentityOptions.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[Identities][TFS] Failed to read project group '{GroupName}' — identity export aborted.", projectGroupName);
            yield break;
        }

        var projectGroups = projectGroupBatches?.Length > 0 ? projectGroupBatches[0] : null;
        if (projectGroups is null || projectGroups.Length == 0 || projectGroups[0] is null)
        {
            _logger.LogWarning(
                "[Identities][TFS] Project group '{GroupName}' not found — no identities exported.", projectGroupName);
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rootGroup = projectGroups[0];

        // The Expanded query returns the group plus all its transitive members in Members[].
        foreach (TfsIdentityDescriptor memberId in rootGroup.Members ?? Array.Empty<TfsIdentityDescriptor>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            TeamFoundationIdentity[][]? resolvedBatches;
            try
            {
                resolvedBatches = ims.ReadIdentities(
                    IdentitySearchFactor.Identifier,
                    new[] { memberId.Identifier },
                    MembershipQuery.None,
                    ReadIdentityOptions.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[Identities][TFS] Could not resolve identity '{Id}' — skipped.", memberId.Identifier);
                continue;
            }

            var resolved = resolvedBatches?.Length > 0 ? resolvedBatches[0] : null;
            if (resolved is null || resolved.Length == 0 || resolved[0] is null)
                continue;

            var identity = resolved[0];

            // Skip groups — only enumerate individual user accounts.
            if (!identity.IsContainer && seen.Add(identity.UniqueName ?? identity.Descriptor.Identifier))
            {
                yield return new AgentIdentityDescriptor(
                    Descriptor: identity.Descriptor.Identifier,
                    DisplayName: identity.DisplayName ?? identity.UniqueName ?? string.Empty,
                    UniqueName: identity.UniqueName ?? string.Empty,
                    SourceType: "tfs",
                    Origin: "tfs",
                    IsActive: identity.IsActive);
            }
        }

        _logger.LogInformation(
            "[Identities][TFS] Identity enumeration complete for project '{Project}'.", projectName);
    }
}
