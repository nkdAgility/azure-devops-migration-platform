// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// In-memory <see cref="IWorkItemTarget"/> for offline testing with <c>target.type: Simulated</c>.
/// Accepts all work items without writing to any external system.
/// Validates input, assigns sequential target IDs, and logs operations internally.
/// </summary>
public sealed class SimulatedWorkItemTarget : IWorkItemTarget
{
    private static readonly string[] DefaultKnownWorkItemTypes =
    [
        "Bug",
        "Task",
        "User Story",
        "Feature",
        "Epic",
        "Issue",
        "Product Backlog Item"
    ];

    private int _nextId = 1;
    private readonly object _lock = new();
    private readonly HashSet<string> _knownWorkItemTypes;
    private readonly Dictionary<int, Dictionary<string, object?>> _workItems = new();
    private readonly Dictionary<int, List<SimulatedAttachment>> _attachmentsByWorkItem = new();
    private readonly Dictionary<string, byte[]> _embeddedImages = new(StringComparer.OrdinalIgnoreCase);

    public SimulatedWorkItemTarget()
        : this(DefaultKnownWorkItemTypes)
    {
    }

    internal SimulatedWorkItemTarget(IEnumerable<string> knownWorkItemTypes)
    {
        _knownWorkItemTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var workItemType in knownWorkItemTypes)
        {
            if (string.IsNullOrWhiteSpace(workItemType))
            {
                continue;
            }

            _knownWorkItemTypes.Add(workItemType.Trim());
        }
    }

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
        {
            assignedId = _nextId++;
            var fieldState = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.ReferenceName))
                {
                    continue;
                }

                fieldState[field.ReferenceName] = field.Value;
            }

            _workItems[assignedId] = fieldState;
        }

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
        lock (_lock)
        {
            if (!_workItems.TryGetValue(targetWorkItemId, out var fieldState))
            {
                throw new InvalidOperationException($"Simulated target work item {targetWorkItemId} does not exist.");
            }

            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.ReferenceName))
                {
                    continue;
                }

                fieldState[field.ReferenceName] = field.Value;
            }
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
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
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
        if (targetWorkItemId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetWorkItemId));
        lock (_lock)
        {
            if (!_workItems.TryGetValue(targetWorkItemId, out var fieldState))
                throw new InvalidOperationException($"Simulated target work item {targetWorkItemId} does not exist.");

            foreach (var field in fields)
            {
                if (string.IsNullOrWhiteSpace(field.ReferenceName))
                    continue;
                fieldState[field.ReferenceName] = field.Value;
            }

            if (!_attachmentsByWorkItem.TryGetValue(targetWorkItemId, out var attachments))
            {
                attachments = new List<SimulatedAttachment>();
                _attachmentsByWorkItem[targetWorkItemId] = attachments;
            }

            foreach (var att in attachmentResults)
            {
                if (!string.IsNullOrEmpty(att.AttachmentUrl))
                    attachments.Add(new SimulatedAttachment(att.FileName, Array.Empty<byte>()));
            }
        }
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
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var attachmentBytes = ReadAllBytes(content);

        lock (_lock)
        {
            if (!_workItems.ContainsKey(targetWorkItemId))
            {
                throw new InvalidOperationException($"Simulated target work item {targetWorkItemId} does not exist.");
            }

            if (!_attachmentsByWorkItem.TryGetValue(targetWorkItemId, out var attachments))
            {
                attachments = new List<SimulatedAttachment>();
                _attachmentsByWorkItem[targetWorkItemId] = attachments;
            }

            attachments.Add(new SimulatedAttachment(fileName, attachmentBytes));
        }

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
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        var imageBytes = ReadAllBytes(content);
        var escapedFileName = Uri.EscapeDataString(fileName);
        lock (_lock)
        {
            _embeddedImages[escapedFileName] = imageBytes;
        }

        var fakeUrl = $"https://simulated.dev.azure.com/attachments/{escapedFileName}";
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
    public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(workItemType))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_knownWorkItemTypes.Contains(workItemType.Trim()));
    }

    /// <inheritdoc/>
    public Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)
    {
        if (targetWorkItemId <= 0)
        {
            return Task.FromResult(false);
        }

        lock (_lock)
        {
            return Task.FromResult(_workItems.ContainsKey(targetWorkItemId));
        }
    }

    private static byte[] ReadAllBytes(Stream content)
    {
        long originalPosition = 0;
        var canRestorePosition = content.CanSeek;
        if (canRestorePosition)
        {
            originalPosition = content.Position;
        }

        try
        {
            if (content is MemoryStream memoryStream)
            {
                return memoryStream.ToArray();
            }

            using var copied = new MemoryStream();
            content.CopyTo(copied);
            return copied.ToArray();
        }
        finally
        {
            if (canRestorePosition)
            {
                content.Position = originalPosition;
            }
        }
    }

    private sealed record SimulatedAttachment(string FileName, byte[] Content);
}
