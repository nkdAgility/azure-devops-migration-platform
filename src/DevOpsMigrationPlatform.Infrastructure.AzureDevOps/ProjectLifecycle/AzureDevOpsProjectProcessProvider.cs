// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;

public sealed class AzureDevOpsProjectProcessProvider : IProjectProcessProvider
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsProjectProcessProvider(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<string> ResolveProcessTypeIdAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ProcessName))
            return KnownProcessIds.Agile;

        if (KnownProcessIds.TryResolve(context.ProcessName, out var known))
            return known;

        var processClient = await _clientFactory.CreateProcessClientAsync(context.Endpoint, cancellationToken).ConfigureAwait(false);
        var processes = await processClient.GetListOfProcessesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var matched = processes.FirstOrDefault(p => MatchesName(p, context.ProcessName));
        if (matched is null)
            throw new InvalidOperationException(
                $"Process '{context.ProcessName}' was not found in Azure DevOps organisation '{context.Endpoint.ResolvedUrl}'.");

        return matched.TypeId.ToString();
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
