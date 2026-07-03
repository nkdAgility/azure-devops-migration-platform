// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi.Models;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.ProjectLifecycle;

public sealed class TfsProjectProcessProvider : IProjectProcessProvider
{
    public async Task<string> ResolveProcessTypeIdAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ProcessName))
            return KnownProcessIds.Agile;

        if (KnownProcessIds.TryResolve(context.ProcessName, out var known))
            return known;

        using var connection = CreateConnection(context.Endpoint);
        var processClient = await connection.GetClientAsync<WorkItemTrackingProcessHttpClient>(cancellationToken).ConfigureAwait(false);
        var processes = await processClient.GetListOfProcessesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var matched = processes.FirstOrDefault(p => MatchesName(p, context.ProcessName));
        if (matched is null)
            throw new InvalidOperationException(
                $"Process '{context.ProcessName}' was not found in Team Foundation Server collection '{context.Endpoint.ResolvedUrl}'.");

        return matched.TypeId.ToString();
    }

    private static VssConnection CreateConnection(OrganisationEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.ResolvedUrl))
            throw new InvalidOperationException("TFS process template endpoint URL was not provided.");

        VssCredentials credentials = endpoint.Authentication.Type == AuthenticationType.AccessToken &&
                                     !string.IsNullOrWhiteSpace(endpoint.Authentication.ResolvedAccessToken)
            ? new VssCredentials(new VssBasicCredential(string.Empty, endpoint.Authentication.ResolvedAccessToken))
            : new VssClientCredentials(true);

        return new VssConnection(new Uri(endpoint.ResolvedUrl), credentials);
    }

    private static bool MatchesName(ProcessInfo process, string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
            return false;

        var normalized = requestedName.Trim();
        return string.Equals(process.Name, normalized, StringComparison.OrdinalIgnoreCase)
               || string.Equals(process.ReferenceName, normalized, StringComparison.OrdinalIgnoreCase);
    }
}
