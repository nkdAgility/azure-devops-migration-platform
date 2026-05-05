// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Server;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="IClassificationTreeReader"/>.
/// Uses <see cref="ICommonStructureService4"/> to enumerate area and iteration nodes from the
/// TFS collection. TFS is export-only so no corresponding <see cref="INodeCreator"/> is needed.
/// </summary>
public sealed class TfsClassificationTreeReader : IClassificationTreeReader
{
    private readonly TfsTeamProjectCollection _collection;
    private readonly ILogger<TfsClassificationTreeReader> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public TfsClassificationTreeReader(
        TfsTeamProjectCollection collection,
        ILogger<TfsClassificationTreeReader> logger,
        ISourceEndpointInfo endpointInfo)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var project = _endpointInfo.Project;

        foreach (var path in EnumerateNodes(project, "Area"))
            yield return path;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var project = _endpointInfo.Project;

        foreach (var node in EnumerateIterationNodeInfos(project))
            yield return node;
    }

    private IEnumerable<string> EnumerateNodes(string project, string structureTypeSuffix)
    {
        var css = _collection.GetService<ICommonStructureService4>();
        ProjectInfo projectInfo;
        try
        {
            projectInfo = css.GetProjectFromName(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation][TFS] Failed to get project {Project} from TFS.", project);
            throw;
        }

        NodeInfo[] structures;
        try
        {
            structures = css.ListStructures(projectInfo.Uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation][TFS] Failed to list structures for project {Project}.", project);
            throw;
        }

        foreach (var node in structures)
        {
            if (!node.StructureType.EndsWith(structureTypeSuffix, StringComparison.OrdinalIgnoreCase))
                continue;

            // TFS returns paths like \ProjectName\Area1 — strip the leading backslash.
            var path = node.Path?.TrimStart('\\') ?? string.Empty;
            if (!string.IsNullOrEmpty(path))
                yield return path;
        }
    }

    private IEnumerable<IterationNodeEntry> EnumerateIterationNodeInfos(string project)
    {
        var css = _collection.GetService<ICommonStructureService4>();
        ProjectInfo projectInfo;
        try
        {
            projectInfo = css.GetProjectFromName(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation][TFS] Failed to get project {Project} from TFS.", project);
            throw;
        }

        NodeInfo[] structures;
        try
        {
            structures = css.ListStructures(projectInfo.Uri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NodeTranslation][TFS] Failed to list structures for project {Project}.", project);
            throw;
        }

        foreach (var node in structures)
        {
            if (!node.StructureType.EndsWith("Iteration", StringComparison.OrdinalIgnoreCase))
                continue;

            var path = node.Path?.TrimStart('\\') ?? string.Empty;
            if (string.IsNullOrEmpty(path)) continue;

            DateTimeOffset? startDate = node.StartDate.HasValue && node.StartDate.Value != DateTime.MinValue
                ? new DateTimeOffset(node.StartDate.Value, TimeSpan.Zero)
                : null;

            DateTimeOffset? finishDate = node.FinishDate.HasValue && node.FinishDate.Value != DateTime.MinValue
                ? new DateTimeOffset(node.FinishDate.Value, TimeSpan.Zero)
                : null;

            yield return new IterationNodeEntry(path, startDate, finishDate, false);
        }
    }

    /// <inheritdoc/>
    public Task<int> CountNodesAsync(string project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult(0);

        try
        {
            var count = EnumerateNodes(project, "Area").Count()
                      + EnumerateIterationNodeInfos(project).Count();
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NodeTranslation][TFS] Failed to count nodes for project {Project}; returning 0.", project);
            return Task.FromResult(0);
        }
    }
}
