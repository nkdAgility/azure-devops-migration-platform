// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Server;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// TFS Object Model implementation of <see cref="INodeCreator"/>.
/// Creates required area/iteration paths by walking parent-child hierarchy in
/// <see cref="ICommonStructureService4"/> and creating missing nodes.
/// </summary>
public sealed class TfsNodeCreator : INodeCreator
{
    private readonly ICommonStructureService4 _classificationService;
    private readonly ILogger<TfsNodeCreator> _logger;
    private readonly string _projectName;
    private readonly string _projectUri;

    public TfsNodeCreator(
        ICommonStructureService4 classificationService,
        ILogger<TfsNodeCreator> logger,
        string projectName,
        string projectUri)
    {
        _classificationService = classificationService ?? throw new ArgumentNullException(nameof(classificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _projectName = !string.IsNullOrWhiteSpace(projectName)
            ? projectName
            : throw new ArgumentException("Project name is required.", nameof(projectName));
        _projectUri = !string.IsNullOrWhiteSpace(projectUri)
            ? projectUri
            : throw new ArgumentException("Project URI is required.", nameof(projectUri));
    }

    /// <inheritdoc />
    public Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = NormalizePath(path, _projectName);
        var existingNodes = BuildPathLookup(nodeType);
        return Task.FromResult(existingNodes.ContainsKey(normalizedPath));
    }

    /// <inheritdoc />
    public Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct)
        => EnsurePathExistsAsync(nodeType, path, startDate: null, finishDate: null, ct);

    /// <inheritdoc />
    public Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct)
    {
        if (startDate is null && finishDate is null)
            return Task.CompletedTask;

        return EnsurePathExistsAsync(ClassificationNodeType.Iteration, path, startDate, finishDate, ct);
    }

    private Task EnsurePathExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var normalizedPath = NormalizePath(path, _projectName);
        if (normalizedPath.Equals(_projectName, StringComparison.OrdinalIgnoreCase))
            return Task.CompletedTask;

        var nodesByPath = BuildPathLookup(nodeType);
        if (nodesByPath.ContainsKey(normalizedPath))
            return Task.CompletedTask;

        var segments = normalizedPath.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var currentPath = _projectName;

        for (var index = 1; index < segments.Length; index++)
        {
            ct.ThrowIfCancellationRequested();

            var segmentName = segments[index];
            var nextPath = $"{currentPath}\\{segmentName}";
            if (nodesByPath.ContainsKey(nextPath))
            {
                currentPath = nextPath;
                continue;
            }

            if (!nodesByPath.TryGetValue(currentPath, out var parentNode))
            {
                throw new InvalidOperationException(
                    $"Parent node '{currentPath}' was not found for {nodeType} path '{normalizedPath}'.");
            }

            var isLeaf = index == segments.Length - 1;
            var start = isLeaf ? startDate?.UtcDateTime : null;
            var finish = isLeaf ? finishDate?.UtcDateTime : null;

            try
            {
                var createdUri = _classificationService.CreateNode(segmentName, parentNode.Uri, start, finish);
                nodesByPath[nextPath] = new NodeInfo
                {
                    Uri = createdUri,
                    Path = "\\" + nextPath,
                    StructureType = GetStructureTypeSuffix(nodeType)
                };

                _logger.LogDebug(
                    "[NodeTranslation][TFS] Created {NodeType} node {Path}.",
                    nodeType,
                    nextPath);
            }
            catch (Exception ex) when (IsConflict(ex))
            {
                _logger.LogDebug(
                    ex,
                    "[NodeTranslation][TFS] Node {Path} already exists (conflict — treating as success).",
                    nextPath);

                nodesByPath = BuildPathLookup(nodeType);
                if (!nodesByPath.ContainsKey(nextPath))
                {
                    throw;
                }
            }

            currentPath = nextPath;
        }

        return Task.CompletedTask;
    }

    private Dictionary<string, NodeInfo> BuildPathLookup(ClassificationNodeType nodeType)
    {
        var suffix = GetStructureTypeSuffix(nodeType);
        var result = new Dictionary<string, NodeInfo>(StringComparer.OrdinalIgnoreCase);
        var structures = _classificationService.ListStructures(_projectUri);

        foreach (var node in structures)
        {
            var normalizedPath = NormalizeStoredPath(node.Path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            if (!normalizedPath.Equals(_projectName, StringComparison.OrdinalIgnoreCase)
                && !StructureTypeMatches(node.StructureType, nodeType, suffix))
            {
                continue;
            }

            result[normalizedPath] = node;
        }

        return result;
    }

    private static string NormalizePath(string path, string fallbackProject)
    {
        var trimmed = (path ?? string.Empty).Trim().TrimStart('\\').Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(trimmed))
            return fallbackProject;

        var projectPrefix = fallbackProject + "\\";
        if (trimmed.Equals(fallbackProject, StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return projectPrefix + trimmed;
    }

    private static string NormalizeStoredPath(string? path)
        => (path ?? string.Empty).Trim().TrimStart('\\').Replace('/', '\\');

    private static string GetStructureTypeSuffix(ClassificationNodeType nodeType)
        => nodeType == ClassificationNodeType.Area ? "Area" : "Iteration";

    private static bool StructureTypeMatches(string? structureType, ClassificationNodeType nodeType, string suffix)
    {
        var value = structureType ?? string.Empty;
        if (value.Length == 0)
            return false;

        if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return true;

        return nodeType == ClassificationNodeType.Area
            ? value.IndexOf("ProjectModelHierarchy", StringComparison.OrdinalIgnoreCase) >= 0
            : value.IndexOf("ProjectLifecycle", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsConflict(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.IndexOf("already exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
               message.IndexOf("TF221122", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
