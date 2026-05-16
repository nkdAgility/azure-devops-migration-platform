// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Simulated in-memory implementation of <see cref="INodeCreator"/>.
/// Tracks created nodes per project in a thread-safe dictionary.
/// All operations are immediately reflected in memory — no external I/O.
/// </summary>
public sealed class SimulatedNodeCreator : INodeCreator
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _areaNodesByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, bool>> _iterationNodesByProject = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SimulatedNodeCreator> _logger;
    private readonly ITargetEndpointInfo _endpointInfo;

    public SimulatedNodeCreator(
        ILogger<SimulatedNodeCreator> logger,
        ITargetEndpointInfo endpointInfo)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
CancellationToken ct)
    {
        var projectNodes = GetOrCreateNodeSet(nodeType, _endpointInfo.Project);
        var normalizedPath = NormalizePath(path, _endpointInfo.Project);
        var exists = projectNodes.ContainsKey(normalizedPath);
        var key = BuildKey(nodeType, normalizedPath, _endpointInfo.Project);
        _logger.LogDebug("[NodeTranslation][Simulated] NodeExistsAsync {Key} = {Exists}.", key, exists);
        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
CancellationToken ct)
    {
        var projectNodes = GetOrCreateNodeSet(nodeType, _endpointInfo.Project);
        foreach (var pathSegment in ExpandHierarchy(path, _endpointInfo.Project))
        {
            projectNodes.TryAdd(pathSegment, true);
        }

        var normalizedPath = NormalizePath(path, _endpointInfo.Project);
        var key = BuildKey(nodeType, normalizedPath, _endpointInfo.Project);
        _logger.LogDebug("[NodeTranslation][Simulated] EnsureExistsAsync {Key}.", key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
CancellationToken ct)
    {
        if (startDate is null && finishDate is null) return Task.CompletedTask;
        _logger.LogDebug("[NodeTranslation][Simulated] SetIterationDatesAsync for {Path} ({Start} – {Finish}).", path, startDate, finishDate);
        return Task.CompletedTask;
    }

    private static string BuildKey(ClassificationNodeType nodeType, string path, string project)
        => $"{nodeType}:{project}:{path}";

    private ConcurrentDictionary<string, bool> GetOrCreateNodeSet(ClassificationNodeType nodeType, string project)
        => nodeType == ClassificationNodeType.Area
            ? _areaNodesByProject.GetOrAdd(project, _ => new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase))
            : _iterationNodesByProject.GetOrAdd(project, _ => new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase));

    private static string NormalizePath(string path, string project)
    {
        var trimmedPath = (path ?? string.Empty).Trim().Replace('/', '\\').Trim('\\');
        if (string.IsNullOrWhiteSpace(trimmedPath))
            return project;

        var projectPrefix = $"{project}\\";
        if (trimmedPath.Equals(project, StringComparison.OrdinalIgnoreCase) ||
            trimmedPath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmedPath;
        }

        return $"{project}\\{trimmedPath}";
    }

    private static string[] ExpandHierarchy(string path, string project)
    {
        var normalizedPath = NormalizePath(path, project);
        var segments = normalizedPath.Split(['\\'], StringSplitOptions.RemoveEmptyEntries);
        var hierarchy = new string[segments.Length];
        for (var index = 0; index < segments.Length; index++)
        {
            hierarchy[index] = string.Join("\\", segments, 0, index + 1);
        }

        return hierarchy;
    }
}
