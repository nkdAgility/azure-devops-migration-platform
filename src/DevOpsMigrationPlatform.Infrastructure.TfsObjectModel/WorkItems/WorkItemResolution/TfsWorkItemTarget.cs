// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;

/// <summary>
/// <see cref="IWorkItemTarget"/> backed by the TFS Object Model.
/// Connects to a TFS or Azure DevOps target project and creates/updates work items
/// using <see cref="WorkItemStore"/> APIs.
/// </summary>
public sealed class TfsWorkItemTarget : IWorkItemTarget
{
    private static readonly HashSet<string> s_excludedFieldReferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.TeamProject",
        "System.State",
        "System.CreatedDate",
        "System.ChangedDate",
        "System.CreatedBy",
        "System.ChangedBy",
        "System.RevisedDate",
        "System.AuthorizedDate"
    };

    private readonly WorkItemStore _store;
    private readonly string _project;
    private readonly ILogger<TfsWorkItemTarget> _logger;

    public TfsWorkItemTarget(
        WorkItemStore store,
        string project,
        ILogger<TfsWorkItemTarget> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<ImportedWorkItemResult> CreateWorkItemAsync(
        string workItemType,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var project = _store.Projects[_project];
        var wit = project.WorkItemTypes[workItemType];
        var workItem = wit.NewWorkItem();

        ApplyFields(workItem, fields);

        try
        {
            workItem.Save();
        }
        catch (ValidationException ex)
        {
            throw new ValidationException($"{ex.Message}. Invalid fields: {DescribeInvalidFields(workItem)}");
        }

        _logger.LogDebug("[TFS Import] Created work item {Id} of type '{Type}'.",
            workItem.Id, workItemType);

        return Task.FromResult(new ImportedWorkItemResult
        {
            TargetWorkItemId = workItem.Id,
            IsNewlyCreated = true
        });
    }

    /// <inheritdoc/>
    public Task UpdateFieldsAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workItem = _store.GetWorkItem(targetWorkItemId);
        ApplyFields(workItem, fields);
        try
        {
            workItem.Save();
        }
        catch (ValidationException ex)
        {
            throw new ValidationException($"{ex.Message}. Invalid fields: {DescribeInvalidFields(workItem)}");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AddLinksAsync(
        int targetWorkItemId,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workItem = _store.GetWorkItem(targetWorkItemId);

        bool changed = false;

        // Related work item links
        foreach (var link in relatedLinks)
        {
            if (string.IsNullOrWhiteSpace(link.ArtifactLinkType))
                continue;

            WorkItemLinkTypeEnd linkTypeEnd;
            try
            {
                linkTypeEnd = _store.WorkItemLinkTypes.LinkTypeEnds[link.ArtifactLinkType];
            }
            catch
            {
                _logger.LogWarning(
                    "[TFS Import] Unknown link type end '{LinkType}' — skipping link on WI {Id}.",
                    link.ArtifactLinkType, targetWorkItemId);
                continue;
            }

            var relatedWorkItem = _store.GetWorkItem(link.RelatedWorkItemId);
            var wil = new WorkItemLink(linkTypeEnd, relatedWorkItem.Id);
            workItem.Links.Add(wil);
            changed = true;
        }

        // External (artifact) links
        foreach (var link in externalLinks)
        {
            if (string.IsNullOrWhiteSpace(link.LinkedArtifactUri))
                continue;

            var regInfo = _store.RegisteredLinkTypes;
            var linkType = regInfo
                .Cast<RegisteredLinkType>()
                .FirstOrDefault(rlt => string.Equals(
                    rlt.Name, link.ArtifactLinkType, StringComparison.OrdinalIgnoreCase));

            if (linkType == null)
            {
                _logger.LogWarning(
                    "[TFS Import] Unknown external link type '{LinkType}' — skipping on WI {Id}.",
                    link.ArtifactLinkType, targetWorkItemId);
                continue;
            }

            var extLink = new ExternalLink(linkType, link.LinkedArtifactUri)
            {
                Comment = link.Comment ?? string.Empty
            };
            workItem.Links.Add(extLink);
            changed = true;
        }

        // Hyperlinks
        foreach (var link in hyperlinks)
        {
            if (string.IsNullOrWhiteSpace(link.Location))
                continue;

            var hLink = new Hyperlink(link.Location)
            {
                Comment = link.Comment ?? string.Empty
            };
            workItem.Links.Add(hLink);
            changed = true;
        }

        if (changed)
            workItem.Save();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ApplyRevisionAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        IReadOnlyList<AttachmentUploadResult> attachmentResults,
        CancellationToken ct)
    {
        // TFS Object Model does not support combining fields + links + attachments in a
        // single API call. Delegate to the existing separate methods internally.
        ct.ThrowIfCancellationRequested();
        UpdateFieldsAsync(targetWorkItemId, fields, ct).GetAwaiter().GetResult();
        AddLinksAsync(targetWorkItemId, relatedLinks, externalLinks, hyperlinks, ct).GetAwaiter().GetResult();
        // Attachment relations are added implicitly by UploadAttachmentAsync for TFS OM
        // (it writes the file and saves the work item directly). At this point the binaries
        // have already been uploaded and attached by the caller via UploadAttachmentAsync,
        // so nothing more is needed here for attachments.
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Write the stream to a temp file because TFS OM requires a file path.
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + "_" + fileName);
        try
        {
            using (var fs = new FileStream(
                tempPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await content.CopyToAsync(fs, 81920, ct).ConfigureAwait(false);
            }

            var workItem = _store.GetWorkItem(targetWorkItemId);
            var attachment = new Attachment(tempPath, fileName);
            workItem.Attachments.Add(attachment);
            workItem.Save();

            // Return attachment URI — TFS uses the file name as identifier.
            return fileName;
        }
        finally
        {
            try { File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <inheritdoc/>
    public Task<string> UploadEmbeddedImageAsync(
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // TFS OM does not have a standalone image-upload API equivalent to ADO's $attachment endpoint.
        // Return an empty string — embedded image rewrites are skipped for TFS targets.
        _logger.LogDebug(
            "[TFS Import] UploadEmbeddedImageAsync is not supported on TFS targets — returning empty URL.");
        return Task.FromResult(string.Empty);
    }

    /// <inheritdoc/>
    public Task CreateCommentAsync(
        int targetWorkItemId,
        string text,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workItem = _store.GetWorkItem(targetWorkItemId);
        workItem["System.History"] = text;
        workItem.Save();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<WorkItemRelations> GetExistingRelationsAsync(
        int targetWorkItemId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var workItem = _store.GetWorkItem(targetWorkItemId);

        var relatedLinks = workItem.Links
            .OfType<WorkItemLink>()
            .Select(l => new RelatedWorkItemLink
            {
                ArtifactLinkType = l.LinkTypeEnd?.Name ?? string.Empty,
                RelatedWorkItemId = l.TargetId,
                Comment = l.Comment
            })
            .ToArray();

        var externalLinks = workItem.Links
            .OfType<ExternalLink>()
            .Select(l => new ExternalWorkItemLink
            {
                ArtifactLinkType = l.ArtifactLinkType?.Name ?? string.Empty,
                LinkedArtifactUri = l.LinkedArtifactUri ?? string.Empty,
                Comment = l.Comment
            })
            .ToArray();

        var hyperlinks = workItem.Links
            .OfType<Hyperlink>()
            .Select(l => new HyperlinkWorkItemLink
            {
                ArtifactLinkType = "Hyperlink",
                Location = l.Location ?? string.Empty,
                Comment = l.Comment
            })
            .ToArray();

        return Task.FromResult(new WorkItemRelations
        {
            RelatedLinks = relatedLinks,
            ExternalLinks = externalLinks,
            Hyperlinks = hyperlinks
        });
    }

    /// <inheritdoc/>
    public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var project = _store.Projects[_project];
        var exists = project.WorkItemTypes
            .Cast<Microsoft.TeamFoundation.WorkItemTracking.Client.WorkItemType>()
            .Any(wit => string.Equals(wit.Name, workItemType, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var workItem = _store.GetWorkItem(targetWorkItemId);
            return Task.FromResult(workItem != null);
        }
        catch (DeniedOrNotExistException)
        {
            return Task.FromResult(false);
        }
    }

    private static void ApplyFields(WorkItem workItem, IReadOnlyList<WorkItemField> fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.ReferenceName))
                continue;
            if (s_excludedFieldReferences.Contains(field.ReferenceName))
                continue;

            try
            {
                workItem[field.ReferenceName] = field.Value;
            }
            catch
            {
                // Best-effort: skip fields that can't be set (e.g. read-only or unknown fields).
            }
        }
    }

    private static string DescribeInvalidFields(WorkItem workItem)
    {
        var builder = new StringBuilder();
        foreach (Field field in workItem.Fields)
        {
            if (field.IsValid)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(field.ReferenceName);
            builder.Append("=");
            builder.Append(field.Value?.ToString() ?? "<null>");
            builder.Append(" (");
            builder.Append(field.Status);
            builder.Append(')');
        }

        return builder.Length == 0 ? "<none>" : builder.ToString();
    }
}
