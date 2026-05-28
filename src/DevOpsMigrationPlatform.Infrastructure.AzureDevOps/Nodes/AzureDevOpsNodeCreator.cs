// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Nodes;

/// <summary>
/// Azure DevOps REST implementation of <see cref="INodeCreator"/>.
/// Creates and queries area/iteration classification nodes in the target ADO project.
/// All operations are idempotent — 409 Conflict responses are treated as success.
/// </summary>
internal sealed class AzureDevOpsNodeCreator : INodeCreator
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ILogger<AzureDevOpsNodeCreator> _logger;
    private readonly ITargetEndpointInfo _endpointInfo;

    public AzureDevOpsNodeCreator(
        IAzureDevOpsClientFactory clientFactory,
        ILogger<AzureDevOpsNodeCreator> logger,
        ITargetEndpointInfo endpointInfo)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct)
    {
        var (project, relativePath) = SplitPath(path, _endpointInfo.Project);
        if (string.IsNullOrEmpty(relativePath)) return true; // project root always exists

        var orgEndpoint = _endpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);
        var group = ToTreeStructureGroup(nodeType);

        try
        {
            await client.GetClassificationNodeAsync(project, group, relativePath, depth: 0, cancellationToken: ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct)
    {
        var (project, relativePath) = SplitPath(path, _endpointInfo.Project);
        if (string.IsNullOrEmpty(relativePath)) return; // project root always exists

        var orgEndpoint = _endpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);
        var group = ToTreeStructureGroup(nodeType);

        await EnsurePathExistsAsync(client, project, group, relativePath, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct)
    {
        if (startDate is null && finishDate is null) return;

        var (project, relativePath) = SplitPath(path, _endpointInfo.Project);
        if (string.IsNullOrEmpty(relativePath)) return;

        var orgEndpoint = _endpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);

        var attributes = new Dictionary<string, object>();
        if (startDate.HasValue)
            attributes["startDate"] = startDate.Value.UtcDateTime;
        if (finishDate.HasValue)
            attributes["finishDate"] = finishDate.Value.UtcDateTime;

        var node = new WorkItemClassificationNode { Attributes = attributes };

        try
        {
            await client.UpdateClassificationNodeAsync(
                node, project, TreeStructureGroup.Iterations, relativePath, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NodeTranslation] Failed to set dates for iteration {Path}.", path);
            throw;
        }
    }

    /// <summary>
    /// Recursively ensures every ancestor of <paramref name="relativePath"/> exists,
    /// then creates the leaf node. Treats 409 Conflict as success.
    /// </summary>
    private async Task EnsurePathExistsAsync(
        WorkItemTrackingHttpClient client,
        string project,
        TreeStructureGroup group,
        string relativePath,
        CancellationToken ct)
    {
        // Check if node already exists
        try
        {
            await client.GetClassificationNodeAsync(project, group, relativePath, depth: 0, cancellationToken: ct)
                .ConfigureAwait(false);
            return; // already exists
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            // Expected — node does not yet exist; proceed to create
        }

        // Ensure parent exists first
        var lastSep = relativePath.LastIndexOf('\\');
        if (lastSep > 0)
        {
            var parentRelative = relativePath[..lastSep];
            await EnsurePathExistsAsync(client, project, group, parentRelative, ct).ConfigureAwait(false);
        }

        // Create this node
        var leafName = lastSep >= 0 ? relativePath[(lastSep + 1)..] : relativePath;
        var parentPath = lastSep > 0 ? relativePath[..lastSep] : null;

        var node = new WorkItemClassificationNode { Name = leafName };

        try
        {
            await client.CreateOrUpdateClassificationNodeAsync(
                node, project, group, path: parentPath, cancellationToken: ct)
                .ConfigureAwait(false);

            _logger.LogDebug("[NodeTranslation] Created node {Group}/{Path} in project {Project}.", group, relativePath, project);
        }
        catch (Exception ex) when (IsConflict(ex))
        {
            _logger.LogDebug("[NodeTranslation] Node {Group}/{Path} already exists (conflict — treating as success).", group, relativePath);
        }
    }

    /// <summary>
    /// Splits a package-format path (ProjectName\Area1\Sub) into (project, Area1\Sub).
    /// Falls back to <paramref name="fallbackProject"/> when no prefix is present.
    /// </summary>
    private static (string project, string relativePath) SplitPath(string path, string fallbackProject)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath)) return (fallbackProject, string.Empty);

        var sep = normalizedPath.IndexOf('\\');
        if (sep < 0)
        {
            // Single segment — either it IS the project name (root) or it's a root-level node name
            if (normalizedPath.Equals(fallbackProject, StringComparison.OrdinalIgnoreCase))
                return (fallbackProject, string.Empty); // project root
            return (fallbackProject, normalizedPath); // single-segment node under project root
        }

        var firstSegment = normalizedPath[..sep];
        var rest = normalizedPath[(sep + 1)..];

        if (firstSegment.Equals(fallbackProject, StringComparison.OrdinalIgnoreCase))
            return (fallbackProject, rest); // strip project prefix

        // Path does not start with project name — treat entire path as relative
        return (fallbackProject, normalizedPath);
    }

    private static TreeStructureGroup ToTreeStructureGroup(ClassificationNodeType nodeType)
        => nodeType == ClassificationNodeType.Area ? TreeStructureGroup.Areas : TreeStructureGroup.Iterations;

    private static bool IsNotFound(Exception ex)
    {
        if (ex is null) return false;
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TF400504", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TF401232", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConflict(Exception ex)
    {
        if (ex is null) return false;
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TF400507", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("TF400506", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
        => path.Replace('/', '\\').Trim().Trim('\\');
}
