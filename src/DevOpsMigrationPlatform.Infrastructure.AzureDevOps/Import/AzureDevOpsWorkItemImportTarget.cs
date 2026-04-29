using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using PlatformWorkItemField = DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.WorkItemField;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Azure DevOps SDK-backed implementation of <see cref="IWorkItemImportTarget"/>.
/// All <see cref="WorkItemTrackingHttpClient"/> calls are wrapped here.
/// Retry with exponential back-off is handled by the underlying VssConnection.
/// </summary>
internal sealed class AzureDevOpsWorkItemImportTarget : IWorkItemImportTarget
{
    private readonly WorkItemTrackingHttpClient _witClient;
    private readonly string _project;

    /// <summary>Organisation URL used to establish the connection to this target.</summary>
    public string OrganisationUrl { get; }

    internal AzureDevOpsWorkItemImportTarget(WorkItemTrackingHttpClient witClient, string project, string organisationUrl)
    {
        _witClient = witClient ?? throw new ArgumentNullException(nameof(witClient));
        _project = project ?? throw new ArgumentNullException(nameof(project));
        OrganisationUrl = organisationUrl ?? throw new ArgumentNullException(nameof(organisationUrl));
    }

    /// <inheritdoc/>
    public async Task<ImportedWorkItemResult> CreateWorkItemAsync(
        string workItemType,
        IReadOnlyList<PlatformWorkItemField> fields,
        CancellationToken ct)
    {
        var patchDocument = BuildFieldPatch(fields, Operation.Add);
        var created = await _witClient
            .CreateWorkItemAsync(patchDocument, _project, workItemType, cancellationToken: ct)
            .ConfigureAwait(false);

        return new ImportedWorkItemResult
        {
            TargetWorkItemId = created.Id ?? throw new InvalidOperationException("Created work item has no ID."),
            IsNewlyCreated = true
        };
    }

    /// <inheritdoc/>
    public async Task UpdateFieldsAsync(
        int targetWorkItemId,
        IReadOnlyList<PlatformWorkItemField> fields,
        CancellationToken ct)
    {
        var patchDocument = BuildFieldPatch(fields, Operation.Add);
        await _witClient
            .UpdateWorkItemAsync(patchDocument, targetWorkItemId, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AddLinksAsync(
        int targetWorkItemId,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        CancellationToken ct)
    {
        var existing = await GetExistingRelationsAsync(targetWorkItemId, ct).ConfigureAwait(false);
        var existingRelatedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingExternalUris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingHyperlinkUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in existing.RelatedLinks)
            existingRelatedIds.Add(r.RelatedWorkItemId.ToString());
        foreach (var e in existing.ExternalLinks)
            existingExternalUris.Add(e.LinkedArtifactUri ?? string.Empty);
        foreach (var h in existing.Hyperlinks)
            existingHyperlinkUrls.Add(h.Location ?? string.Empty);

        var patch = new JsonPatchDocument();

        foreach (var rel in relatedLinks)
        {
            if (existingRelatedIds.Contains(rel.RelatedWorkItemId.ToString())) continue;
            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = rel.ArtifactLinkType,
                    url = $"/_apis/wit/workItems/{rel.RelatedWorkItemId}",
                    attributes = rel.Comment is not null ? new { comment = rel.Comment } : null
                }
            });
        }

        foreach (var ext in externalLinks)
        {
            if (string.IsNullOrEmpty(ext.LinkedArtifactUri) || existingExternalUris.Contains(ext.LinkedArtifactUri)) continue;
            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = ext.ArtifactLinkType,
                    url = ext.LinkedArtifactUri,
                    attributes = ext.Comment is not null ? new { comment = ext.Comment } : null
                }
            });
        }

        foreach (var hyper in hyperlinks)
        {
            if (string.IsNullOrEmpty(hyper.Location) || existingHyperlinkUrls.Contains(hyper.Location)) continue;
            patch.Add(new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = "Hyperlink",
                    url = hyper.Location,
                    attributes = hyper.Comment is not null ? new { comment = hyper.Comment } : null
                }
            });
        }

        if (patch.Count == 0) return;

        await _witClient
            .UpdateWorkItemAsync(patch, targetWorkItemId, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        var attachment = await _witClient
            .CreateAttachmentAsync(content, fileName: fileName, cancellationToken: ct)
            .ConfigureAwait(false);

        var attachmentUrl = attachment.Url
            ?? throw new InvalidOperationException($"Attachment upload for {fileName} returned no URL.");

        var patch = new JsonPatchDocument
        {
            new JsonPatchOperation
            {
                Operation = Operation.Add,
                Path = "/relations/-",
                Value = new
                {
                    rel = "AttachedFile",
                    url = attachmentUrl,
                    attributes = new { name = fileName }
                }
            }
        };
        await _witClient
            .UpdateWorkItemAsync(patch, targetWorkItemId, cancellationToken: ct)
            .ConfigureAwait(false);

        return attachmentUrl;
    }

    /// <inheritdoc/>
    public async Task<string> UploadEmbeddedImageAsync(
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        var attachment = await _witClient
            .CreateAttachmentAsync(content, fileName: fileName, cancellationToken: ct)
            .ConfigureAwait(false);

        return attachment.Url
            ?? throw new InvalidOperationException($"Embedded image upload for {fileName} returned no URL.");
    }

    /// <inheritdoc/>
    public async Task CreateCommentAsync(
        int targetWorkItemId,
        string text,
        CancellationToken ct)
    {
        await _witClient
            .AddCommentAsync(
                new CommentCreate { Text = text },
                _project,
                targetWorkItemId,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<WorkItemRelations> GetExistingRelationsAsync(
        int targetWorkItemId,
        CancellationToken ct)
    {
        var workItem = await _witClient
            .GetWorkItemAsync(targetWorkItemId, expand: WorkItemExpand.Relations, cancellationToken: ct)
            .ConfigureAwait(false);

        var relations = workItem.Relations ?? new List<WorkItemRelation>();

        var related = new List<RelatedWorkItemLink>();
        var external = new List<ExternalWorkItemLink>();
        var hyperlinks = new List<HyperlinkWorkItemLink>();

        foreach (var r in relations)
        {
            if (string.Equals(r.Rel, "Hyperlink", StringComparison.OrdinalIgnoreCase))
            {
                hyperlinks.Add(new HyperlinkWorkItemLink
                {
                    ArtifactLinkType = r.Rel ?? string.Empty,
                    Location = r.Url ?? string.Empty
                });
            }
            else if (string.Equals(r.Rel, "ArtifactLink", StringComparison.OrdinalIgnoreCase))
            {
                external.Add(new ExternalWorkItemLink
                {
                    ArtifactLinkType = r.Rel ?? string.Empty,
                    LinkedArtifactUri = r.Url ?? string.Empty
                });
            }
            else if (r.Url is not null && int.TryParse(r.Url.Split('/').LastOrDefault(), out var relatedId))
            {
                related.Add(new RelatedWorkItemLink
                {
                    ArtifactLinkType = r.Rel ?? string.Empty,
                    RelatedWorkItemId = relatedId
                });
            }
        }

        return new WorkItemRelations
        {
            RelatedLinks = related,
            ExternalLinks = external,
            Hyperlinks = hyperlinks
        };
    }

    /// <inheritdoc/>
    public async Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)
    {
        try
        {
            var wi = await _witClient
                .GetWorkItemAsync(targetWorkItemId, cancellationToken: ct)
                .ConfigureAwait(false);
            return wi is not null;
        }
        catch (Microsoft.VisualStudio.Services.Common.VssServiceException ex)
            when (ex.Message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
               || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
    }

    // --- Helpers ---

    private static JsonPatchDocument BuildFieldPatch(IReadOnlyList<PlatformWorkItemField> fields, Operation op)
    {
        var doc = new JsonPatchDocument();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.ReferenceName)) continue;
            doc.Add(new JsonPatchOperation
            {
                Operation = op,
                Path = $"/fields/{field.ReferenceName}",
                Value = field.Value
            });
        }
        return doc;
    }
}
