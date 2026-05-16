// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

internal sealed class AzureDevOpsWorkItemTypeReadinessTarget : IWorkItemTypeReadinessTarget
{
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly string _project;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);
    private HashSet<string>? _cachedWorkItemTypes;

    public AzureDevOpsWorkItemTypeReadinessTarget(WorkItemTrackingHttpClient witClient, string project)
    {
        _witClient = witClient ?? throw new ArgumentNullException(nameof(witClient));
        _project = project ?? throw new ArgumentNullException(nameof(project));
    }

    public async Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workItemType))
        {
            return false;
        }

        var cachedWorkItemTypes = _cachedWorkItemTypes;
        if (cachedWorkItemTypes is null)
        {
            await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                cachedWorkItemTypes = _cachedWorkItemTypes;
                if (cachedWorkItemTypes is null)
                {
                    var types = await _witClient.GetWorkItemTypesAsync(_project, cancellationToken: ct).ConfigureAwait(false);
                    cachedWorkItemTypes = new HashSet<string>(
                        types
                            .Select(t => t.Name)
                            .Where(name => !string.IsNullOrWhiteSpace(name))
                            .Select(name => name!.Trim()),
                        StringComparer.OrdinalIgnoreCase);
                    _cachedWorkItemTypes = cachedWorkItemTypes;
                }
            }
            finally
            {
                _cacheLock.Release();
            }
        }

        return cachedWorkItemTypes.Contains(workItemType.Trim());
    }
}
