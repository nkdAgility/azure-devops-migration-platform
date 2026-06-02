// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Nodes;

/// <summary>
/// Ensures required area and iteration paths exist before work item revision replay begins.
/// Supports referenced-path creation and optional full source-tree replication.
/// </summary>
public sealed class NodeReadinessOrchestrator
{
    private const string ReferencedPathsFile = "referenced-paths.json";
    private const string SourceTreeFile = "source-tree.json";
    private const string ModuleName = "Nodes";
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly JsonSerializerOptions s_jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IPackageAccess _packageAccess;
    private readonly INodeTranslationTool _nodeTranslationTool;
    private readonly INodeCreator _nodeCreator;
    private readonly ILogger<NodeReadinessOrchestrator> _logger;
    private readonly NodeTranslationOptions? _nodeTranslationOptions;
    private readonly IImportCreatedNodeStateStore? _importCreatedNodeStateStore;
    private readonly string _organisation;
    private readonly string _project;

    public NodeReadinessOrchestrator(
        IPackageAccess packageAccess,
        INodeTranslationTool nodeTranslationTool,
        INodeCreator nodeCreator,
        ILogger<NodeReadinessOrchestrator> logger,
        string organisation,
        string project,
        IOptions<NodeTranslationOptions>? nodeTranslationOptions = null,
        IImportCreatedNodeStateStore? importCreatedNodeStateStore = null)
    {
        _packageAccess = packageAccess ?? throw new ArgumentNullException(nameof(packageAccess));
        _nodeTranslationTool = nodeTranslationTool ?? throw new ArgumentNullException(nameof(nodeTranslationTool));
        _nodeCreator = nodeCreator ?? throw new ArgumentNullException(nameof(nodeCreator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _organisation = organisation ?? throw new ArgumentNullException(nameof(organisation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _nodeTranslationOptions = nodeTranslationOptions?.Value;
        _importCreatedNodeStateStore = importCreatedNodeStateStore;
    }

    public async Task ExecuteAsync(
        ProjectMapping context,
        bool replicateSourceTree,
        CancellationToken ct)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        using var activity = s_activitySource.StartActivity("nodes.import.readiness");
        activity?.SetTag("replicateSourceTree", replicateSourceTree);

        _logger.LogInformation(
            "[NodeReadiness] Preparing required paths for target project {Project}. ReplicateSourceTree={ReplicateSourceTree}.",
            context.TargetProjectName,
            replicateSourceTree);

        var organisation = _organisation;
        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = "unknown";
        }

        var project = _project;
        if (string.IsNullOrWhiteSpace(project))
        {
            project = !string.IsNullOrWhiteSpace(context.SourceProjectName)
                ? context.SourceProjectName
                : context.TargetProjectName;
        }

        if (string.IsNullOrWhiteSpace(project))
        {
            project = "unknown";
        }

        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_importCreatedNodeStateStore is not null)
        {
            var checkpointedNodes = await _importCreatedNodeStateStore.GetRecordedCreatedNodeKeysAsync(ct).ConfigureAwait(false);
            foreach (var checkpointedNode in checkpointedNodes)
            {
                processed.Add(checkpointedNode);
            }
        }

        var referenced = await ReadArtifactAsync<ReferencedPathsArtifact>(organisation, project, ReferencedPathsFile, ct).ConfigureAwait(false);
        if (referenced is null)
        {
            var referencedPathsStrategy = new ReferencedPathsFromWorkItemsStrategy(
                _packageAccess,
                _logger,
                organisation,
                project);
            referenced = await referencedPathsStrategy
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
            var snapshot = await ReadArtifactAsync<ClassificationTreeSnapshot>(organisation, project, SourceTreeFile, ct).ConfigureAwait(false);
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
                        if (_importCreatedNodeStateStore is not null)
                        {
                            await _importCreatedNodeStateStore.RecordCreatedNodePathAsync(ClassificationNodeType.Iteration, targetPath, ct).ConfigureAwait(false);
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
            if (_importCreatedNodeStateStore is not null)
            {
                await _importCreatedNodeStateStore.RecordCreatedNodePathAsync(nodeType, targetPath, ct).ConfigureAwait(false);
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

    private async Task<T?> ReadArtifactAsync<T>(string organisation, string project, string fileName, CancellationToken ct)
    {
        var payload = await _packageAccess
            .RequestContentAsync(
                new PackageContentContext(
                    PackageContentKind.Artefact,
                    Organisation: organisation,
                    Project: project,
                    Module: ModuleName,
                    Address: new RelativePathAddress(fileName)),
                ct)
            .ConfigureAwait(false);

        if (payload is not null)
        {
            return await DeserializeArtifactAsync<T>(payload, ct).ConfigureAwait(false);
        }

        var metadataPayload = await EnumerateClassificationMetadataAsync(organisation, project, fileName, ct).ConfigureAwait(false);
        if (metadataPayload is null)
            return default;

        return await DeserializeArtifactAsync<T>(metadataPayload, ct).ConfigureAwait(false);
    }

    private async Task<PackagePayload?> EnumerateClassificationMetadataAsync(string organisation, string project, string fileName, CancellationToken ct)
    {
        await foreach (var enumeratedPath in _packageAccess.EnumerateContentAsync(
                           new PackageContentContext(
                               PackageContentKind.Collection,
                               Organisation: organisation,
                               Project: project,
                               Module: ModuleName,
                               IsCollectionRequest: true),
                           ct).ConfigureAwait(false))
        {
            var normalizedEnumeratedPath = enumeratedPath.Replace('\\', '/');
            var candidateFileName = normalizedEnumeratedPath.EndsWith($"/{fileName}", StringComparison.OrdinalIgnoreCase)
                ? fileName
                : null;
            if (candidateFileName is null)
                continue;

            var payload = await _packageAccess
                .RequestContentAsync(
                    new PackageContentContext(
                        PackageContentKind.Artefact,
                        Organisation: _organisation,
                        Project: _project,
                        Module: ModuleName,
                        Address: new RelativePathAddress(candidateFileName)),
                    ct)
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
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return default;

        return JsonSerializer.Deserialize<T>(content, s_jsonOptions);
    }

    private static string BuildNodeKey(ClassificationNodeType nodeType, string path)
        => $"{nodeType}:{path}";
}
