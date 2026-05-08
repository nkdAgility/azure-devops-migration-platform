// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Processes a single revision folder through four sequential import stages:
/// <list type="number">
///   <item>Stage A — <c>CreatedOrUpdated</c>: create or resolve target work item, record ID mapping.</item>
///   <item>Stage B — <c>AppliedFields</c>: apply all fields (with identity resolution).</item>
///   <item>Stage C — <c>AppliedLinks</c>: add related links, external links, and hyperlinks (skip duplicates).</item>
///   <item>Stage D — <c>UploadedAttachments</c>: stream attachment binaries to the target (skip already uploaded).</item>
/// </list>
/// Cursor is written after each stage. On resume, stages already completed for this folder are skipped.
/// All extension enabled flags are respected: if <c>Revisions: false</c>, the caller must skip this processor.
/// </summary>
public sealed class RevisionFolderProcessor : IRevisionFolderProcessor
{
    private readonly IWorkItemImportTarget _target;
    private readonly IIdMapStore _idMapStore;
    private readonly ICheckpointingService _checkpointing;
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly IArtefactStore _artefactStore;
    private readonly ILogger<RevisionFolderProcessor> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly string? _jobId;
    private readonly IFieldTransformTool? _fieldTransformTool;
    private readonly INodeTranslationTool? _nodeStructureTool;
    private readonly ProjectMapping? _nodeTranslationContext;
    private readonly NodeTranslationOptions? _nodeStructureOptions;

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RevisionFolderProcessor(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        IArtefactStore artefactStore,
        ILogger<RevisionFolderProcessor> logger,
        IPlatformMetrics? metrics = null,
        string? jobId = null,
        IFieldTransformTool? fieldTransformTool = null,
        INodeTranslationTool? nodeStructureTool = null,
        ProjectMapping? nodeStructureContext = null,
        NodeTranslationOptions? nodeStructureOptions = null)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _idMapStore = idMapStore ?? throw new ArgumentNullException(nameof(idMapStore));
        _checkpointing = checkpointing ?? throw new ArgumentNullException(nameof(checkpointing));
        _identityLookupTool = identityLookupTool;
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics;
        _jobId = jobId;
        _fieldTransformTool = fieldTransformTool;
        _nodeStructureTool = nodeStructureTool;
        _nodeTranslationContext = nodeStructureContext;
        _nodeStructureOptions = nodeStructureOptions;

        if (_fieldTransformTool == null)
            _logger.LogWarning("[WorkItems] IFieldTransformTool is not registered — field transforms will be skipped for all revisions. Call AddFieldTransformToolServices() in your DI setup to enable field transforms.");
        if (_nodeStructureTool == null)
            _logger.LogWarning("[WorkItems] INodeTranslationTool is not registered — area/iteration path translation will be skipped for all revisions. Call AddNodeTranslationToolServices() in your DI setup to enable path translation.");
    }

    /// <summary>
    /// Process a single revision folder, resuming from <paramref name="resumeAtStage"/> if provided.
    /// </summary>
    /// <param name="folderPath">Relative folder path, e.g. <c>WorkItems/2026-01-15/638760000000000001-42-3</c>.</param>
    /// <param name="ext">Module extension flags controlling which stages run.</param>
    /// <param name="resumeAtStage">
    /// If not null, skip all stages that lexicographically precede this stage value.
    /// Pass <see langword="null"/> to start from Stage A.
    /// </param>
    /// <param name="resolutionStrategy">Strategy for live fallback ID lookup after Stage A.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ProcessAsync(
        string folderPath,
        WorkItemsModuleExtensions ext,
        string? resumeAtStage,
        IWorkItemResolutionStrategy resolutionStrategy,
        CancellationToken ct)
    {
        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);

        var revisionJson = await _artefactStore.ReadAsync($"{folderPath}/revision.json", ct).ConfigureAwait(false);
        if (revisionJson is null)
        {
            _logger.LogWarning("[WorkItems] revision.json not found in {Folder} — skipping.", folderPath);
            return;
        }

        var revision = JsonSerializer.Deserialize<WorkItemRevision>(revisionJson, _jsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialise revision.json in {folderPath}");

        // Record import-side payload complexity metrics.
        if (_metrics != null)
        {
            var importTags = MetricsTagList.Create(_jobId ?? "not-set", "import", "workitems");
            _metrics.RecordFieldCount(revision.Fields.Count, importTags);
            _metrics.RecordAttachmentCount(revision.Attachments.Count, importTags);
            _metrics.RecordLinkCount(
                revision.ExternalLinks.Count + revision.RelatedLinks.Count + revision.Hyperlinks.Count,
                importTags);
            _metrics.RecordPayloadBytes(revisionJson.Length, importTags);
        }

        using var revActivity = ActivitySource.StartActivity("revision.import", ActivityKind.Internal);
        revActivity?.SetTag("workitem.id", revision.WorkItemId);
        revActivity?.SetTag("revision.index", revision.RevisionIndex);

        // Stage A — CreatedOrUpdated
        if (ShouldRunStage(CursorStage.CreatedOrUpdated, resumeAtStage))
        {
            var targetId = await _idMapStore.GetTargetWorkItemIdAsync(revision.WorkItemId, ct).ConfigureAwait(false);

            if (targetId is null)
            {
                // Live fallback lookup via strategy
                targetId = await resolutionStrategy.ResolveSingleAsync(revision.WorkItemId, ct).ConfigureAwait(false);
            }
            else
            {
                // Existing mapping: verify the target still exists (guard against deleted targets)
                var exists = await _target.WorkItemExistsAsync(targetId.Value, ct).ConfigureAwait(false);
                if (!exists)
                {
                    _logger.LogWarning(
                        "[WorkItems] Source {SourceId} mapped to deleted target {TargetId} — recording skip and advancing cursor.",
                        revision.WorkItemId, targetId.Value);
                    await _idMapStore.RecordSkippedRevisionAsync(revision.WorkItemId, "TargetWorkItemDeleted", ct).ConfigureAwait(false);
                    await WriteCursorAsync(folderPath, CursorStage.Completed, ct).ConfigureAwait(false);
                    return;
                }
            }

            if (targetId is null)
            {
                var workItemType = GetWorkItemType(revision.Fields);
                var result = await _target.CreateWorkItemAsync(workItemType, revision.Fields, ct).ConfigureAwait(false);
                targetId = result.TargetWorkItemId;
                await _idMapStore.SetWorkItemMappingAsync(revision.WorkItemId, targetId.Value, ct).ConfigureAwait(false);
                await resolutionStrategy.WriteProvenanceAsync(revision.WorkItemId, targetId.Value, ct).ConfigureAwait(false);
                _logger.LogDebug("[WorkItems] Created target WI {TargetId} for source {SourceId}", targetId, revision.WorkItemId);
            }
            else
            {
                _logger.LogDebug("[WorkItems] Source {SourceId} already mapped to target {TargetId} — updating.", revision.WorkItemId, targetId);
            }

            await WriteCursorAsync(folderPath, CursorStage.CreatedOrUpdated, ct).ConfigureAwait(false);
        }

        // Resolve target ID for remaining stages
        var resolvedTargetId = await _idMapStore.GetTargetWorkItemIdAsync(revision.WorkItemId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No target ID mapping for source work item {revision.WorkItemId} after Stage A.");

        // Stage B — AppliedFields
        if (ShouldRunStage(CursorStage.AppliedFields, resumeAtStage))
        {
            // Identity resolution — NOTE: IIdentityMappingService.Resolve is synchronous per the existing interface.
            // Full identity mapping logic is added in T031 (US4). For now, pass fields as-is.
            var fields = ApplyIdentityResolution(revision.Fields);

            // Embedded images — upload and rewrite URLs if extension enabled
            if (ext.EmbeddedImages.Enabled && revision.EmbeddedImages.Count > 0)
            {
                fields = await RewriteEmbeddedImageUrlsAsync(fields, revision.EmbeddedImages, folderPath, ct).ConfigureAwait(false);
            }

            // Field transforms — apply if tool is registered and enabled for Import phase
            if (_fieldTransformTool != null && _fieldTransformTool.IsEnabledForPhase(FieldTransformPhase.Import))
            {
                var workItemType = GetWorkItemType(revision.Fields);
                var fieldDict = FieldsToDict(fields);
                var transformResult = _fieldTransformTool.ApplyTransforms(
                    fieldDict,
                    new FieldTransformContext(revision.WorkItemId, revision.RevisionIndex, workItemType, FieldTransformPhase.Import));
                fields = DictToFields(transformResult.Fields);
                _logger.LogDebug(
                    "[WorkItems] Applied {ActionCount} field transform actions to source WI {WorkItemId} revision {RevisionIndex}.",
                    transformResult.Actions.Count, revision.WorkItemId, revision.RevisionIndex);
            }

            // NodeTranslation path translation
            if (_nodeStructureTool != null && _nodeStructureTool.IsEnabled && _nodeTranslationContext != null)
            {
                if (!TryApplyNodeTranslation(fields, _nodeTranslationContext, out var translatedFields))
                {
                    // Revision skipped — external/unresolvable path with SkipOnUnresolvable* enabled
                    await _idMapStore.RecordSkippedRevisionAsync(revision.WorkItemId, "UnresolvablePath", ct).ConfigureAwait(false);
                    await WriteCursorAsync(folderPath, CursorStage.Completed, ct).ConfigureAwait(false);
                    return;
                }
                fields = translatedFields;
            }

            await _target.UpdateFieldsAsync(resolvedTargetId, fields, ct).ConfigureAwait(false);
            await WriteCursorAsync(folderPath, CursorStage.AppliedFields, ct).ConfigureAwait(false);
        }

        // Stage C — AppliedLinks
        if (ext.LinksEnabled && ShouldRunStage(CursorStage.AppliedLinks, resumeAtStage))
        {
            await _target.AddLinksAsync(
                resolvedTargetId,
                revision.RelatedLinks,
                revision.ExternalLinks,
                revision.Hyperlinks,
                ct).ConfigureAwait(false);
            await WriteCursorAsync(folderPath, CursorStage.AppliedLinks, ct).ConfigureAwait(false);
        }
        else if (!ext.LinksEnabled && ShouldRunStage(CursorStage.AppliedLinks, resumeAtStage))
        {
            // Skip stage but still advance cursor so resume logic is consistent
            await WriteCursorAsync(folderPath, CursorStage.AppliedLinks, ct).ConfigureAwait(false);
        }

        // Stage D — UploadedAttachments
        if (ext.AttachmentsEnabled && ShouldRunStage(CursorStage.UploadedAttachments, resumeAtStage))
        {
            foreach (var attachment in revision.Attachments)
            {
                var existingId = await _idMapStore.GetAttachmentIdAsync(
                    revision.WorkItemId, revision.RevisionIndex, attachment.RelativePath, ct).ConfigureAwait(false);

                if (existingId is not null)
                {
                    _logger.LogDebug("[WorkItems] Attachment {File} already uploaded — skipping.", attachment.RelativePath);
                    continue;
                }

                var binaryPath = $"{folderPath}/{attachment.RelativePath}";
                await using var stream = await _artefactStore.ReadBinaryAsync(binaryPath, ct).ConfigureAwait(false);
                if (stream is null)
                {
                    _logger.LogWarning("[WorkItems] Attachment binary {Path} not found — skipping.", binaryPath);
                    continue;
                }

                var targetAttachmentId = await _target.UploadAttachmentAsync(
                    resolvedTargetId, attachment.OriginalName, stream, ct).ConfigureAwait(false);

                await _idMapStore.SetAttachmentMappingAsync(
                    revision.WorkItemId, revision.RevisionIndex, attachment.RelativePath, targetAttachmentId, ct)
                    .ConfigureAwait(false);
            }

            await WriteCursorAsync(folderPath, CursorStage.UploadedAttachments, ct).ConfigureAwait(false);
        }
        else if (!ext.AttachmentsEnabled && ShouldRunStage(CursorStage.UploadedAttachments, resumeAtStage))
        {
            await WriteCursorAsync(folderPath, CursorStage.UploadedAttachments, ct).ConfigureAwait(false);
        }

        // Inline comments
        if (ext.Comments.Enabled)
        {
            await ProcessInlineCommentsAsync(resolvedTargetId, folderPath, ct).ConfigureAwait(false);
        }

        // Final cursor — Completed
        await WriteCursorAsync(folderPath, CursorStage.Completed, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies node structure path translation. Returns <c>false</c> if the revision should be skipped.
    /// </summary>
    private bool TryApplyNodeTranslation(
        IReadOnlyList<WorkItemField> fields,
        ProjectMapping context,
        out IReadOnlyList<WorkItemField> result)
    {
        result = fields;
        var output = new List<WorkItemField>(fields.Count);
        bool anyTranslated = false;

        foreach (var field in fields)
        {
            if (field.ReferenceName is "System.AreaPath" or "System.IterationPath"
                && field.Value is string pathValue
                && !string.IsNullOrEmpty(pathValue))
            {
                var translation = _nodeStructureTool!.TranslatePath(field.ReferenceName, pathValue, context);

                if (translation.IsExternalPath)
                {
                    bool isArea = field.ReferenceName == "System.AreaPath";
                    bool skipEnabled = isArea
                        ? (_nodeStructureOptions?.SkipOnUnresolvableArea ?? false)
                        : (_nodeStructureOptions?.SkipOnUnresolvableIteration ?? false);

                    string fieldLabel = isArea ? "area" : "iteration";

                    if (skipEnabled)
                    {
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogWarning(
                                "[NodeTranslation] Revision skipped — external (not anchored in source project) {FieldLabel} path: {Path}",
                                fieldLabel, pathValue);
                        return false;
                    }
                    else
                    {
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogError(
                                "[NodeTranslation] Unresolvable {FieldLabel} path: {Path} — import aborted (set SkipOnUnresolvable{CapLabel} to skip instead)",
                                fieldLabel, pathValue, isArea ? "Area" : "Iteration");
                        throw new InvalidOperationException(
                            $"[NodeTranslation] Unresolvable {fieldLabel} path: '{pathValue}'. " +
                            $"Set SkipOnUnresolvable{(isArea ? "Area" : "Iteration")}: true to skip instead.");
                    }
                }

                if (translation.TargetPath != null && !string.Equals(translation.TargetPath, pathValue, StringComparison.Ordinal))
                {
                    output.Add(new WorkItemField { ReferenceName = field.ReferenceName, Value = translation.TargetPath });
                    anyTranslated = true;
                    _logger.LogTrace(
                        "[NodeTranslation] Path translated: {Field} = {Target} (mapHit={MapHit}, swap={Swap}, external={External})",
                        field.ReferenceName, translation.TargetPath,
                        translation.MatchedByMap, translation.MatchedByProjectSwap, translation.IsExternalPath);
                }
                else
                {
                    output.Add(field);
                }
            }
            else
            {
                output.Add(field);
            }
        }

        result = anyTranslated ? output : fields;
        return true;
    }

    // --- Helpers ---

    private static bool ShouldRunStage(string stage, string? resumeAtStage)
    {
        if (resumeAtStage is null) return true;
        return string.CompareOrdinal(stage, resumeAtStage) >= 0;
    }

    private static string GetWorkItemType(IReadOnlyList<WorkItemField> fields)
    {
        foreach (var f in fields)
        {
            if (string.Equals(f.ReferenceName, "System.WorkItemType", StringComparison.OrdinalIgnoreCase))
                return f.Value?.ToString() ?? "Task";
        }
        return "Task";
    }

    private IReadOnlyList<WorkItemField> ApplyIdentityResolution(IReadOnlyList<WorkItemField> fields)
    {
        // Identity-type fields resolved via IIdentityLookupTool when enabled.
        var identityFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System.AssignedTo", "System.ChangedBy", "System.CreatedBy"
        };

        var result = new List<WorkItemField>(fields.Count);
        foreach (var field in fields)
        {
            if (identityFields.Contains(field.ReferenceName) && field.Value is string identity)
            {
                var resolved = _identityLookupTool?.IsEnabled == true ? _identityLookupTool.Resolve(identity) : identity;
                result.Add(new WorkItemField { ReferenceName = field.ReferenceName, Value = resolved });
            }
            else
            {
                result.Add(field);
            }
        }
        return result;
    }

    private async Task<IReadOnlyList<WorkItemField>> RewriteEmbeddedImageUrlsAsync(
        IReadOnlyList<WorkItemField> fields,
        IReadOnlyList<EmbeddedImageMetadata> images,
        string folderPath,
        CancellationToken ct)
    {
        var urlMap = new Dictionary<string, string>(images.Count, StringComparer.Ordinal);
        foreach (var img in images)
        {
            var imgPath = $"{folderPath}/{img.RelativePath}";
            await using var imgStream = await _artefactStore.ReadBinaryAsync(imgPath, ct).ConfigureAwait(false);
            if (imgStream is null)
            {
                _logger.LogWarning("[WorkItems] Embedded image {Path} not found — skipping URL rewrite.", imgPath);
                continue;
            }
            var targetUrl = await _target.UploadEmbeddedImageAsync(img.RelativePath, imgStream, ct).ConfigureAwait(false);
            urlMap[img.OriginalUrl] = targetUrl;
        }

        if (urlMap.Count == 0) return fields;

        var result = new List<WorkItemField>(fields.Count);
        foreach (var field in fields)
        {
            if (field.Value is string html && html.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                var rewritten = html;
                foreach (var (original, target) in urlMap)
                    rewritten = rewritten.Replace(original, target, StringComparison.Ordinal);
                result.Add(new WorkItemField { ReferenceName = field.ReferenceName, Value = rewritten });
            }
            else
            {
                result.Add(field);
            }
        }
        return result;
    }

    private async Task ProcessInlineCommentsAsync(int targetWorkItemId, string folderPath, CancellationToken ct)
    {
        var commentJson = await _artefactStore.ReadAsync($"{folderPath}/comment.json", ct).ConfigureAwait(false);
        if (commentJson is null) return;

        var comments = JsonSerializer.Deserialize<List<WorkItemComment>>(commentJson, _jsonOptions);
        if (comments is null || comments.Count == 0) return;

        foreach (var comment in comments)
        {
            if (comment.IsDeleted) continue;
            var text = comment.RenderedText ?? comment.Text;
            await _target.CreateCommentAsync(targetWorkItemId, text, ct).ConfigureAwait(false);
        }
    }

    private Task WriteCursorAsync(string folderPath, string stage, CancellationToken ct)
        => _checkpointing.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = stage,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);

    private static Dictionary<string, object?> FieldsToDict(IReadOnlyList<WorkItemField> fields)
    {
        var dict = new Dictionary<string, object?>(fields.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var f in fields)
            dict[f.ReferenceName] = f.Value;
        return dict;
    }

    private static IReadOnlyList<WorkItemField> DictToFields(IReadOnlyDictionary<string, object?> dict)
    {
        var result = new List<WorkItemField>(dict.Count);
        foreach (var kvp in dict)
            result.Add(new WorkItemField { ReferenceName = kvp.Key, Value = kvp.Value?.ToString() });
        return result;
    }
}
#endif

