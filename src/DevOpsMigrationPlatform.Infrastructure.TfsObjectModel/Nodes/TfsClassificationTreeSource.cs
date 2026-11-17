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

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Nodes;

/// <summary>
/// TFS Object Model implementation of <see cref="IClassificationTreeSource"/>.
/// Uses <see cref="ICommonStructureService4"/> to enumerate area and iteration nodes from the
/// TFS collection.
/// </summary>
public sealed class TfsClassificationTreeSource : IClassificationTreeSource
{
    private readonly TfsTeamProjectCollection _collection;
    private readonly ILogger<TfsClassificationTreeSource> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public TfsClassificationTreeSource(
        TfsTeamProjectCollection collection,
        ILogger<TfsClassificationTreeSource> logger,
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

        foreach (var node in EnumerateTree(project, ClassificationNodesXml.IsAreaRoot))
            yield return node.Path;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        var project = _endpointInfo.Project;

        foreach (var node in EnumerateTree(project, ClassificationNodesXml.IsIterationRoot))
            yield return node;
    }

    // ListStructures returns only the two roots (ProjectModelHierarchy = areas,
    // ProjectLifecycle = iterations); GetNodesXml returns each root's full tree (#169).
    private IEnumerable<IterationNodeEntry> EnumerateTree(string project, Func<string?, bool> isRoot)
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

        foreach (var root in structures)
        {
            if (!isRoot(root.StructureType))
                continue;

            System.Xml.XmlElement tree;
            try
            {
                tree = css.GetNodesXml(new[] { root.Uri }, true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[NodeTranslation][TFS] Failed to read the {Structure} tree for project {Project}.", root.StructureType, project);
                throw;
            }

            foreach (var node in ClassificationNodesXml.Flatten(tree))
                yield return node;
        }
    }
    /// <inheritdoc/>
    public Task<int> CountNodesAsync(string project, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult(0);

        try
        {
            var count = EnumerateTree(project, ClassificationNodesXml.IsAreaRoot).Count()
                      + EnumerateTree(project, ClassificationNodesXml.IsIterationRoot).Count();
            return Task.FromResult(count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[NodeTranslation][TFS] Failed to count nodes for project {Project}; returning 0.", project);
            return Task.FromResult(0);
        }
    }
}
