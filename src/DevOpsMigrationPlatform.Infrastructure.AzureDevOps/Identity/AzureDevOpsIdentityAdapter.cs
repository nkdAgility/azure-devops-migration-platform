// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Services.Identity;
using Microsoft.VisualStudio.Services.Identity.Client;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Identity;

/// <summary>
/// Azure DevOps Services implementation of <see cref="IIdentityAdapter"/>. Searches the live
/// <b>target</b> tenant by account name (UPN/email) and display name via the Identity Service
/// SDK (<see cref="IdentityHttpClient.ReadIdentitiesAsync(IdentitySearchFilter, string, QueryMembership, ReadIdentitiesOptions, System.Collections.Generic.IEnumerable{System.Guid}, bool, bool, bool, object, CancellationToken)"/>),
/// for use by <c>IdentitiesOrchestrator.PrepareAsync</c> during the Prepare phase.
/// </summary>
internal sealed class AzureDevOpsIdentityAdapter : IIdentityAdapter
{
    private static readonly string[] s_accountPropertyKeys = { "Account", "Mail", "SignInAddress", "DirectoryAlias" };

    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly ILogger<AzureDevOpsIdentityAdapter> _logger;

    public AzureDevOpsIdentityAdapter(
        IAzureDevOpsClientFactory clientFactory,
        ITargetEndpointInfo targetEndpointInfo,
        ILogger<AzureDevOpsIdentityAdapter> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct)
        => SearchAsync(IdentitySearchFilter.AccountName, upn, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)
        => SearchAsync(IdentitySearchFilter.DisplayName, displayName, ct);

    private async Task<IReadOnlyList<IdentityCandidate>> SearchAsync(IdentitySearchFilter filter, string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<IdentityCandidate>();

        try
        {
            var endpoint = _targetEndpointInfo.ToOrganisationEndpoint();
            var client = await _clientFactory.CreateIdentityClientAsync(endpoint, ct).ConfigureAwait(false);
            var identities = await client.ReadIdentitiesAsync(filter, query, QueryMembership.None, cancellationToken: ct).ConfigureAwait(false);

            var candidates = new List<IdentityCandidate>();
            if (identities is not null)
            {
                foreach (var identity in identities)
                {
                    if (identity is null)
                        continue;

                    candidates.Add(new IdentityCandidate(
                        Descriptor: identity.Descriptor?.ToString() ?? identity.Id.ToString(),
                        Upn: GetAccountName(identity),
                        DisplayName: identity.ProviderDisplayName));
                }
            }

            return candidates;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[Identities/ADO] Identity search ({Filter}) for '{Query}' failed; returning no candidates — resolution falls through to the default.",
                filter, query);
            return Array.Empty<IdentityCandidate>();
        }
    }

    private static string? GetAccountName(Microsoft.VisualStudio.Services.Identity.Identity identity)
    {
        if (identity.Properties is null)
            return null;

        foreach (var key in s_accountPropertyKeys)
        {
            if (identity.Properties.TryGetValue(key, out var value)
                && value is string s
                && !string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }
}
