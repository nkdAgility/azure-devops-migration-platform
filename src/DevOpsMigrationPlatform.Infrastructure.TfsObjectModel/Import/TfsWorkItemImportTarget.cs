// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// TFS Object Model import target adapter used by prepare-time validation.
/// </summary>
public sealed class TfsWorkItemImportTarget : IWorkItemImportTarget
{
    private readonly HashSet<string> _workItemTypes;

    public TfsWorkItemImportTarget(IEnumerable<string> workItemTypes)
    {
        if (workItemTypes is null)
        {
            throw new ArgumentNullException(nameof(workItemTypes));
        }

        _workItemTypes = new HashSet<string>(
            workItemTypes
                .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
                .Select(typeName => typeName.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workItemType))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_workItemTypes.Contains(workItemType.Trim()));
    }

    public Task<ImportedWorkItemResult> CreateWorkItemAsync(string workItemType, IReadOnlyList<WorkItemField> fields, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task UpdateFieldsAsync(int targetWorkItemId, IReadOnlyList<WorkItemField> fields, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task AddLinksAsync(int targetWorkItemId, IReadOnlyList<RelatedWorkItemLink> relatedLinks, IReadOnlyList<ExternalWorkItemLink> externalLinks, IReadOnlyList<HyperlinkWorkItemLink> hyperlinks, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task<string> UploadAttachmentAsync(int targetWorkItemId, string fileName, Stream content, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task<string> UploadEmbeddedImageAsync(string fileName, Stream content, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task CreateCommentAsync(int targetWorkItemId, string text, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task<WorkItemRelations> GetExistingRelationsAsync(int targetWorkItemId, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    public Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)
        => throw CreateUnsupportedOperationException();

    private static InvalidOperationException CreateUnsupportedOperationException()
        => new("TfsWorkItemImportTarget only supports work item type existence checks.");
}
