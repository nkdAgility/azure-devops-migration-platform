// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Work-item capability: replays inline work-item comments onto the target work item during import. The
/// comment-replay business rule, expressed as an <see cref="IModuleExtension"/> port, independent of the
/// checkpoint/resume delivery mechanism that drives it.
/// When an <see cref="IWorkItemCommentSourceFactory"/> is injected, this extension also supports export:
/// it fetches comment versions for edit/delete revisions and writes them as comment.json.
/// </summary>
public sealed class CommentsWorkItemExtension : IModuleExtension
{
    private readonly CommentsExtensionOptions _options;
    private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;
    private readonly ILogger<CommentsWorkItemExtension>? _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions _exportJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public CommentsWorkItemExtension(
        IOptions<CommentsExtensionOptions> options,
        IWorkItemCommentSourceFactory? commentSourceFactory = null,
        ILogger<CommentsWorkItemExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _commentSourceFactory = commentSourceFactory;
        _logger = logger;
    }

    public string Module => "WorkItems";
    public string Name => "Comments";
    public int Order => 500;
    public bool SupportsExport => _commentSourceFactory is not null && _options.Enabled;
    public bool SupportsImport => true;
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemRevisionExportContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemRevisionExportContext)}.", nameof(context));

        if (_commentSourceFactory == null)
            return;

        if (ctx.SourceEndpoint == null)
        {
            _logger?.LogWarning("[Comments] WI {WorkItemId} rev {RevisionIndex}: no source endpoint available — skipping comment export.",
                ctx.WorkItemId, ctx.RevisionIndex);
            return;
        }

        if (!IsCommentEditOrDeleteRevision(ctx.Revision))
            return;

        try
        {
            var commentSource = _commentSourceFactory.Create(ctx.SourceEndpoint, ctx.ProjectName);
            var matchingComments = new List<WorkItemComment>();

            await foreach (var comment in commentSource.GetCommentsAsync(
                ctx.WorkItemId, includeDeleted: true, ct))
            {
                var deltaSeconds = Math.Abs((comment.ModifiedDate - ctx.Revision.ChangedDate).TotalSeconds);
                if (deltaSeconds <= 1.0)
                    matchingComments.Add(comment);
            }

            if (matchingComments.Count > 0)
            {
                var commentJson = JsonSerializer.Serialize(matchingComments, _exportJsonOptions);
                var commentPath = $"{ctx.FolderPath}comment.json";
                var revisionFolderPath = GetRevisionFolderPath(commentPath);
                var fileName = GetFileName(commentPath);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(commentJson), writable: false);
                await ctx.Package.PersistContentAsync(
                    new PackageContentContext(
                        PackageContentKind.Artefact,
                        Organisation: ctx.Organisation,
                        Project: ctx.ProjectName,
                        Module: "WorkItems",
                        Address: new WorkItemAttachmentAddress(revisionFolderPath, fileName)),
                    new PackagePayload(stream, "application/json"),
                    ct).ConfigureAwait(false);

                _logger?.LogDebug("[Comments] WI {WorkItemId} rev {RevisionIndex}: wrote {Count} comment(s) to comment.json.",
                    ctx.WorkItemId, ctx.RevisionIndex, matchingComments.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[Comments] Inline comment fetch failed for WI {WorkItemId} rev {RevisionIndex}.",
                ctx.WorkItemId, ctx.RevisionIndex);
        }
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not WorkItemExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(WorkItemExtensionContext)}.", nameof(context));
        if (ctx.Target is null)
            throw new ArgumentException("WorkItemExtensionContext.Target is required for import.", nameof(context));
        if (ctx.ReadTextAsync is null)
            throw new ArgumentException("WorkItemExtensionContext.ReadTextAsync is required for comment import.", nameof(context));

        var commentJson = await ctx.ReadTextAsync($"{ctx.FolderPath}/comment.json", ct).ConfigureAwait(false);
        if (commentJson is null) return;

        var comments = JsonSerializer.Deserialize<List<WorkItemComment>>(commentJson, _jsonOptions);
        if (comments is null || comments.Count == 0) return;

        foreach (var comment in comments)
        {
            if (comment.IsDeleted) continue;
            var text = comment.RenderedText ?? comment.Text;
            await ctx.Target.CreateCommentAsync(ctx.TargetWorkItemId, text, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns true when the revision represents a comment edit or delete (not a new comment addition).
    /// Comment edits/deletes change System.CommentCount without adding System.History text.
    /// RevisionIndex 0 is always excluded (creation revision has unreliable CommentCount delta).
    /// </summary>
    internal static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
    {
        if (revision.RevisionIndex == 0)
            return false;

        bool hasHistory = false;
        bool hasCommentCount = false;

        foreach (var field in revision.Fields)
        {
            if (field.ReferenceName == "System.History" && !string.IsNullOrEmpty(field.Value))
                hasHistory = true;
            if (field.ReferenceName == "System.CommentCount")
                hasCommentCount = true;
        }

        return hasCommentCount && !hasHistory;
    }

    private static string GetRevisionFolderPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("WorkItems/".Length);

        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(0, lastSlash) : normalized;
    }

    private static string GetFileName(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized.Substring(lastSlash + 1) : normalized;
    }
}
