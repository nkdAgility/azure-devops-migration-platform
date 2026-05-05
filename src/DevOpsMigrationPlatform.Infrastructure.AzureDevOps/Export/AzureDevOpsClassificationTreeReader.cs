// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;

/// <summary>
/// Azure DevOps REST implementation of <see cref="IClassificationTreeReader"/>.
/// Reads the full area and iteration classification trees from the source project using the ADO WIT API.
/// </summary>
internal sealed class AzureDevOpsClassificationTreeReader : IClassificationTreeReader
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ILogger<AzureDevOpsClassificationTreeReader> _logger;

    public AzureDevOpsClassificationTreeReader(
        IAzureDevOpsClientFactory clientFactory,
        ISourceEndpointInfo sourceEndpointInfo,
        ILogger<AzureDevOpsClassificationTreeReader> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var project = _sourceEndpointInfo.Project;
        var orgEndpoint = _sourceEndpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);

        WorkItemClassificationNode root;
        try
        {
            root = await client.GetClassificationNodeAsync(
                project, TreeStructureGroup.Areas, path: null, depth: int.MaxValue, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation] Failed to fetch area classification tree for project {Project}.", project);
            throw;
        }

        foreach (var path in FlattenNodePaths(root))
            yield return path;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var project = _sourceEndpointInfo.Project;
        var orgEndpoint = _sourceEndpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);

        WorkItemClassificationNode root;
        try
        {
            root = await client.GetClassificationNodeAsync(
                project, TreeStructureGroup.Iterations, path: null, depth: int.MaxValue, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation] Failed to fetch iteration classification tree for project {Project}.", project);
            throw;
        }

        foreach (var entry in FlattenIterationNodes(root))
            yield return entry;
    }

    /// <summary>Recursively flattens node paths, stripping the leading backslash from ADO paths.</summary>
    private static IEnumerable<string> FlattenNodePaths(WorkItemClassificationNode node)
    {
        // ADO returns Path with a leading backslash: \ProjectName\Area1
        // Our package format is without the leading backslash: ProjectName\Area1
        var path = node.Path?.TrimStart('\\') ?? string.Empty;
        if (!string.IsNullOrEmpty(path))
            yield return path;

        if (node.Children is null) yield break;
        foreach (var child in node.Children)
            foreach (var p in FlattenNodePaths(child))
                yield return p;
    }

    /// <summary>Recursively flattens iteration nodes, extracting dates from node attributes.</summary>
    private static IEnumerable<IterationNodeEntry> FlattenIterationNodes(WorkItemClassificationNode node)
    {
        var path = node.Path?.TrimStart('\\') ?? string.Empty;
        if (!string.IsNullOrEmpty(path))
        {
            DateTimeOffset? startDate = null;
            DateTimeOffset? finishDate = null;

            if (node.Attributes is not null)
            {
                if (node.Attributes.TryGetValue("startDate", out var startObj) && startObj is DateTime start && start != DateTime.MinValue)
                    startDate = new DateTimeOffset(start, TimeSpan.Zero);

                if (node.Attributes.TryGetValue("finishDate", out var finishObj) && finishObj is DateTime finish && finish != DateTime.MinValue)
                    finishDate = new DateTimeOffset(finish, TimeSpan.Zero);
            }

            yield return new IterationNodeEntry(path, startDate, finishDate, false);
        }

        if (node.Children is null) yield break;
        foreach (var child in node.Children)
            foreach (var e in FlattenIterationNodes(child))
                yield return e;
    }

    /// <inheritdoc/>
    public async Task<int> CountNodesAsync(string project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project))
            return 0;

        var orgEndpoint = _sourceEndpointInfo.ToOrganisationEndpoint();
        var client = await _clientFactory.CreateWorkItemClientAsync(orgEndpoint, ct).ConfigureAwait(false);

        try
        {
            var areaRoot = await client.GetClassificationNodeAsync(
                project, TreeStructureGroup.Areas, path: null, depth: int.MaxValue, cancellationToken: ct)
                .ConfigureAwait(false);
            var iterRoot = await client.GetClassificationNodeAsync(
                project, TreeStructureGroup.Iterations, path: null, depth: int.MaxValue, cancellationToken: ct)
                .ConfigureAwait(false);
            return FlattenNodePaths(areaRoot).Count() + FlattenIterationNodes(iterRoot).Count();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NodeTranslation] Failed to count classification nodes for project {Project}; returning 0.", project);
            return 0;
        }
    }
}
