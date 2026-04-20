using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// In-memory <see cref="IWorkItemImportTarget"/> for offline testing with <c>target.type: Simulated</c>.
/// Accepts all work items without writing to any external system.
/// Validates input, assigns sequential target IDs, and logs operations internally.
/// </summary>
public sealed class SimulatedWorkItemImportTarget : IWorkItemImportTarget
{
    private int _nextId = 1;
    private readonly object _lock = new();

    /// <inheritdoc/>
    public Task<ImportedWorkItemResult> CreateWorkItemAsync(
        string workItemType,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workItemType))
            throw new ArgumentException("workItemType must not be empty.", nameof(workItemType));

        int assignedId;
        lock (_lock)
            assignedId = _nextId++;

        return Task.FromResult(new ImportedWorkItemResult
        {
            TargetWorkItemId = assignedId,
            IsNewlyCreated = true
        });
    }

    /// <inheritdoc/>
    public Task UpdateFieldsAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct)
    {
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
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
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName must not be empty.", nameof(fileName));

        // Deterministic fake attachment ID: simulated://<wid>/<fileName>
        var fakeId = $"simulated://{targetWorkItemId}/{Uri.EscapeDataString(fileName)}";
        return Task.FromResult(fakeId);
    }

    /// <inheritdoc/>
    public Task<string> UploadEmbeddedImageAsync(
        string fileName,
        Stream content,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("fileName must not be empty.", nameof(fileName));

        var fakeUrl = $"https://simulated.dev.azure.com/attachments/{Uri.EscapeDataString(fileName)}";
        return Task.FromResult(fakeUrl);
    }

    /// <inheritdoc/>
    public Task CreateCommentAsync(
        int targetWorkItemId,
        string text,
        CancellationToken ct)
    {
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<WorkItemRelations> GetExistingRelationsAsync(
        int targetWorkItemId,
        CancellationToken ct)
        => Task.FromResult(new WorkItemRelations());

    /// <inheritdoc/>
    public Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)
        => Task.FromResult(true);
}
