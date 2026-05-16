// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// Creates <see cref="TfsWorkItemTypeReadinessTarget"/> from the currently active TFS job services.
/// </summary>
public sealed class TfsActiveJobWorkItemTypeReadinessTargetFactory : IWorkItemTypeReadinessTargetFactory
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobWorkItemTypeReadinessTargetFactory(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    public Task<IWorkItemTypeReadinessTarget> CreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var services = _activeServices.Require();
        var availableProjectNames = services.WorkItemStore.Projects
            .Cast<Project>()
            .Select(project => project.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToArray();

        var projectName = ResolveProjectName(services.Endpoint.GetProject(), availableProjectNames);
        Project project;
        try
        {
            project = services.WorkItemStore.Projects[projectName];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            project = services.WorkItemStore.Projects
                .Cast<Project>()
                .FirstOrDefault(candidate => string.Equals(candidate.Name?.Trim(), projectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Target project '{projectName}' could not be resolved from TFS metadata. " +
                    "Refresh TFS project metadata and rerun Prepare.",
                    ex);
        }

        var workItemTypes = project.WorkItemTypes
            .Cast<WorkItemType>()
            .Select(workItemType => workItemType.Name)
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Select(typeName => typeName.Trim())
            .ToArray();

        return Task.FromResult<IWorkItemTypeReadinessTarget>(new TfsWorkItemTypeReadinessTarget(workItemTypes));
    }

    internal static string ResolveProjectName(string projectName, IReadOnlyCollection<string> availableProjectNames)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException(
                "Target project name is missing in the TFS endpoint configuration. " +
                "Set target.project to a valid TFS project name before running Prepare.");
        }

        var normalizedProjectName = projectName.Trim();
        if (availableProjectNames.Count == 0)
        {
            throw new InvalidOperationException(
                $"TFS metadata retrieval could not find any projects while resolving target project '{normalizedProjectName}'.");
        }

        if (!availableProjectNames.Contains(normalizedProjectName, StringComparer.OrdinalIgnoreCase))
        {
            var orderedNames = string.Join(", ", availableProjectNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"Target project '{normalizedProjectName}' was not found in TFS metadata. " +
                $"Available projects: {orderedNames}.");
        }

        return availableProjectNames.First(name => string.Equals(name, normalizedProjectName, StringComparison.OrdinalIgnoreCase));
    }
}
