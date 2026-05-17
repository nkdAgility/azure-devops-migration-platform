// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Ensures required area and iteration paths exist before work item revision replay begins.
/// Supports referenced-path creation and optional full source-tree replication.
/// </summary>
public sealed class NodeReadinessOrchestrator
{
    private const string ReferencedPathsPath = "Nodes/referenced-paths.json";
    private const string SourceTreePath = "Nodes/source-tree.json";
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPackageAccess _packageAccess;
    private readonly INodeTranslationTool _nodeTranslationTool;
    private readonly INodeCreator _nodeCreator;
    private readonly ReferencedPathsFromWorkItemsStrategy _referencedPathsFromWorkItemsStrategy;
    private readonly ILogger<NodeReadinessOrchestrator> _logger;
    private readonly NodeTranslationOptions? _nodeTranslationOptions;
    private readonly ImportCheckpointService? _importCheckpointService;

    public NodeReadinessOrchestrator(
        IPackageAccess packageAccess,
        INodeTranslationTool nodeTranslationTool,
        INodeCreator nodeCreator,
        ILogger<NodeReadinessOrchestrator> logger,
        IOptions<NodeTranslationOptions>? nodeTranslationOptions = null,
        ImportCheckpointService? importCheckpointService = null)
    {
        _packageAccess = packageAccess ?? throw new ArgumentNullException(nameof(packageAccess));
        _nodeTranslationTool = nodeTranslationTool ?? throw new ArgumentNullException(nameof(nodeTranslationTool));
        _nodeCreator = nodeCreator ?? throw new ArgumentNullException(nameof(nodeCreator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nodeTranslationOptions = nodeTranslationOptions?.Value;
        _importCheckpointService = importCheckpointService;
        _referencedPathsFromWorkItemsStrategy = new ReferencedPathsFromWorkItemsStrategy(_packageAccess, _logger);
    }

    public async Task ExecuteAsync(
        ProjectMapping context,
        bool replicateSourceTree,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var activity = s_activitySource.StartActivity("nodes.import.readiness");
        activity?.SetTag("replicateSourceTree", replicateSourceTree);

        _logger.LogInformation(
            "[NodeReadiness] Preparing required paths for target project {Project}. ReplicateSourceTree={ReplicateSourceTree}.",
            context.TargetProjectName,
            replicateSourceTree);

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_importCheckpointService is not null)
        {
            var cursor = await _importCheckpointService.ReadCursorAsync(ct).ConfigureAwait(false);
            if (cursor is not null)
            {
                var checkpointedNodes = await _importCheckpointService.GetCreatedNodePathKeysAsync(ct).ConfigureAwait(false);
                foreach (var checkpointedNode in checkpointedNodes)
                {
                    processed.Add(checkpointedNode);
                }
            }
        }

        var referenced = await ReadArtifactAsync<ReferencedPathsArtifact>(ReferencedPathsPath, ct).ConfigureAwait(false);
        if (referenced is null)
        {
            referenced = await _referencedPathsFromWorkItemsStrategy
                .CollectDistinctPathsAsync(ct)
                .ConfigureAwait(false);
        }

        if (referenced is not null)
        {
            await EnsureTranslatedPathsAsync(
                ClassificationNodeType.Area,
                "System.AreaPath",
                referenced.AreaPaths,
                context,
                processed,
                ct).ConfigureAwait(false);

            await EnsureTranslatedPathsAsync(
                ClassificationNodeType.Iteration,
                "System.IterationPath",
                referenced.IterationPaths,
                context,
                processed,
                ct).ConfigureAwait(false);
        }

        if (replicateSourceTree)
        {
            var snapshot = await ReadArtifactAsync<ClassificationTreeSnapshot>(SourceTreePath, ct).ConfigureAwait(false);
            if (snapshot is not null)
            {
                await EnsureTranslatedPathsAsync(
                    ClassificationNodeType.Area,
                    "System.AreaPath",
                    snapshot.AreaNodes,
                    context,
                    processed,
                    ct).ConfigureAwait(false);

                foreach (var iteration in snapshot.IterationNodes)
                {
                    if (string.IsNullOrWhiteSpace(iteration.Path))
                        continue;

                    var targetPath = ResolveNodePathOrThrow(
                        "System.IterationPath",
                        iteration.Path,
                        context);
                    if (targetPath is null)
                        continue;

                    var key = BuildNodeKey(ClassificationNodeType.Iteration, targetPath);
                    if (processed.Add(key))
                    {
                        await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, targetPath, ct).ConfigureAwait(false);
                        if (_importCheckpointService is not null)
                        {
                            await _importCheckpointService.SetCreatedNodePathAsync(ClassificationNodeType.Iteration, targetPath, ct).ConfigureAwait(false);
                        }
                    }

                    if (iteration.StartDate.HasValue || iteration.FinishDate.HasValue)
                    {
                        await _nodeCreator
                            .SetIterationDatesAsync(targetPath, iteration.StartDate, iteration.FinishDate, ct)
                            .ConfigureAwait(false);
                    }
                }
            }
        }

        activity?.SetTag("nodes.processed", processed.Count);
        _logger.LogInformation("[NodeReadiness] Prepared {Count} required node paths.", processed.Count);
    }

    private async Task EnsureTranslatedPathsAsync(
        ClassificationNodeType nodeType,
        string fieldName,
        IReadOnlyList<string> sourcePaths,
        ProjectMapping context,
        HashSet<string> processed,
        CancellationToken ct)
    {
        foreach (var sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            var targetPath = ResolveNodePathOrThrow(fieldName, sourcePath, context);
            if (targetPath is null)
                continue;

            var key = BuildNodeKey(nodeType, targetPath);
            if (!processed.Add(key))
                continue;

            await _nodeCreator.EnsureExistsAsync(nodeType, targetPath, ct).ConfigureAwait(false);
            if (_importCheckpointService is not null)
            {
                await _importCheckpointService.SetCreatedNodePathAsync(nodeType, targetPath, ct).ConfigureAwait(false);
            }
        }
    }

    private string? ResolveNodePathOrThrow(
        string fieldName,
        string sourcePath,
        ProjectMapping context)
    {
        var translation = _nodeTranslationTool.TranslatePath(fieldName, sourcePath, context);
        if (!translation.IsExternalPath)
            return translation.TargetPath ?? sourcePath;

        bool isArea = string.Equals(fieldName, "System.AreaPath", StringComparison.OrdinalIgnoreCase);
        bool skipEnabled = isArea
            ? (_nodeTranslationOptions?.SkipOnUnresolvableArea ?? false)
            : (_nodeTranslationOptions?.SkipOnUnresolvableIteration ?? false);
        string fieldLabel = isArea ? "area" : "iteration";

        if (skipEnabled)
        {
            using (DataClassificationScope.Begin(DataClassification.Customer))
            {
                _logger.LogWarning(
                    "[NodeTranslation] Node readiness skipped external {FieldLabel} path: {Path}",
                    fieldLabel,
                    sourcePath);
            }

            return null;
        }

        using (DataClassificationScope.Begin(DataClassification.Customer))
        {
            _logger.LogError(
                "[NodeTranslation] Unresolvable {FieldLabel} path during node readiness: {Path} — import aborted (set SkipOnUnresolvable{CapLabel} to skip instead)",
                fieldLabel,
                sourcePath,
                isArea ? "Area" : "Iteration");
        }

        throw new InvalidOperationException(
            $"[NodeTranslation] Unresolvable {fieldLabel} path during node readiness: '{sourcePath}'. " +
            $"Set SkipOnUnresolvable{(isArea ? "Area" : "Iteration")}: true to skip instead.");
    }

    private async Task<T?> ReadArtifactAsync<T>(string relativePath, CancellationToken ct)
    {
        var payload = await _packageAccess
            .RequestContentAsync(new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(relativePath)), ct)
            .ConfigureAwait(false);

        if (payload is not null)
        {
            return await DeserializeArtifactAsync<T>(payload, ct).ConfigureAwait(false);
        }

        var metadataPayload = await EnumerateClassificationMetadataAsync(relativePath, ct).ConfigureAwait(false);
        if (metadataPayload is null)
            return default;

        return await DeserializeArtifactAsync<T>(metadataPayload, ct).ConfigureAwait(false);
    }

    private async Task<PackagePayload?> EnumerateClassificationMetadataAsync(string relativePath, CancellationToken ct)
    {
        var normalizedPath = relativePath.Replace('\\', '/');
        var separatorIndex = normalizedPath.LastIndexOf('/');
        if (separatorIndex <= 0 || separatorIndex >= normalizedPath.Length - 1)
            return null;

        var parentPath = normalizedPath[..separatorIndex];
        var fileName = normalizedPath[(separatorIndex + 1)..];

        await foreach (var enumeratedPath in _packageAccess.EnumerateContentAsync(
                           new PackageContentContext(
                               PackageContentKind.Collection,
                               Address: new RelativePathAddress(parentPath),
                               IsCollectionRequest: true),
                           ct).ConfigureAwait(false))
        {
            var normalizedEnumeratedPath = enumeratedPath.Replace('\\', '/');
            var candidatePath = normalizedEnumeratedPath.EndsWith($"/{fileName}", StringComparison.OrdinalIgnoreCase)
                ? normalizedEnumeratedPath
                : null;
            if (candidatePath is null)
                continue;

            var payload = await _packageAccess
                .RequestContentAsync(new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(candidatePath)), ct)
                .ConfigureAwait(false);
            if (payload is not null)
                return payload;
        }

        return null;
    }

    private static async Task<T?> DeserializeArtifactAsync<T>(PackagePayload payload, CancellationToken ct)
    {
        if (payload.Content.CanSeek)
            payload.Content.Position = 0;

        using var reader = new StreamReader(payload.Content);
        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return default;

        return JsonSerializer.Deserialize<T>(content, s_jsonOptions);
    }

    private static string BuildNodeKey(ClassificationNodeType nodeType, string path)
        => $"{nodeType}:{path}";

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
#endif
