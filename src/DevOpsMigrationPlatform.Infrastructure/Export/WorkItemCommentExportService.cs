using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

#if !NET481

/// <summary>
/// Exports work item comments to the migration package.
/// Each comment version is written to a separate folder:
/// WorkItems/yyyy-MM-dd/&lt;ticks&gt;-&lt;workItemId&gt;-c&lt;commentId&gt;/comment.json
/// 
/// Resumability is handled by <see cref="WorkItemExportOrchestrator"/> — comments are
/// exported immediately after each work item's revisions are complete, so the main
/// WorkItems cursor naturally provides resumability without a separate comment cursor.
/// </summary>
public class WorkItemCommentExportService : IWorkItemCommentExportService
{
    private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;
    private readonly IArtefactStore _artefactStore;
    private readonly IOptions<WorkItemsScopeParameters> _scopeParameters;
    private readonly ILogger<WorkItemCommentExportService> _logger;

    public WorkItemCommentExportService(
        IWorkItemCommentSourceFactory? commentSourceFactory,
        IArtefactStore artefactStore,
        IOptions<WorkItemsScopeParameters> scopeParameters,
        ILogger<WorkItemCommentExportService> logger)
    {
        _commentSourceFactory = commentSourceFactory;
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _scopeParameters = scopeParameters ?? throw new ArgumentNullException(nameof(scopeParameters));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Exports all comments for a single work item.
    /// Called by <see cref="WorkItemExportOrchestrator"/> immediately after finishing
    /// that work item's revisions, ensuring atomic export per work item.
    /// </summary>
    public async Task ExportAsync(
        int workItemId,
        string organisationUrl,
        string project,
        string pat,
        CancellationToken cancellationToken)
    {
        if (!_scopeParameters.Value.Comments.Enabled)
        {
            _logger.LogDebug("Comments export is disabled. Skipping work item {workItemId}", workItemId);
            return;
        }

        if (_commentSourceFactory == null)
        {
            _logger.LogWarning("Comment source factory not available. Skipping comment export for work item {workItemId}", workItemId);
            return;
        }

        var commentSource = _commentSourceFactory.Create(organisationUrl, project, pat);

        int commentCount = 0;
        try
        {
            // Fetch and write each comment
            await foreach (var comment in commentSource.GetCommentsAsync(
                workItemId,
                _scopeParameters.Value.Comments.IncludeDeleted,
                cancellationToken))
            {
                await WriteCommentAsync(workItemId, comment, cancellationToken);
                commentCount++;
            }

            if (commentCount > 0)
            {
                _logger.LogDebug(
                    "Exported {commentCount} comment(s) for work item {workItemId}",
                    commentCount, workItemId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export comments for work item {workItemId}", workItemId);
            throw;
        }
    }

    /// <summary>
    /// Serializes a single comment to comment.json in the appropriate date-bucketed folder.
    /// </summary>
    private async Task WriteCommentAsync(
        int workItemId,
        WorkItemComment comment,
        CancellationToken cancellationToken)
    {
        // Build folder path: WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-c<commentId>/
        var folderDate = comment.ModifiedDate.ToString("yyyy-MM-dd");
        var ticks = comment.ModifiedDate.UtcTicks;
        var folderName = $"{ticks}-{workItemId}-c{comment.CommentId}";
        var folderPath = Path.Combine("WorkItems", folderDate, folderName);

        // Serialize comment to JSON
        var json = JsonSerializer.Serialize(comment, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var commentFilePath = Path.Combine(folderPath, "comment.json");
        await _artefactStore.WriteAsync(commentFilePath, json, cancellationToken);

        _logger.LogDebug(
            "Wrote comment {commentId} v{version} for work item {workItemId} to {path}",
            comment.CommentId, comment.Version, workItemId, commentFilePath);
    }
}

#endif
