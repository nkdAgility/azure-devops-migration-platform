using System;
using System.Collections.Concurrent;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Attachments;

/// <summary>
/// Holds the download URLs for work item attachments discovered during a single export run.
/// Populated by <see cref="AzureDevOpsWorkItemRevisionSource"/> as each revision is mapped
/// and consumed by <see cref="AzureDevOpsAttachmentBinarySource"/> when the orchestrator
/// requests the attachment bytes.
/// Scoped to the lifetime of one export operation (register as scoped DI service).
/// </summary>
internal sealed class AzureDevOpsAttachmentRegistry
{
    // Key: (workItemId, revisionIndex, originalName)  Value: download URL
    private readonly ConcurrentDictionary<AttachmentKey, string> _urls = new();

    /// <summary>
    /// Records the download URL for an attachment.
    /// </summary>
    public void Register(int workItemId, int revisionIndex, string originalName, string downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return;

        _urls[new AttachmentKey(workItemId, revisionIndex, originalName)] = downloadUrl;
    }

    /// <summary>
    /// Returns the download URL previously registered for the attachment, or <c>null</c> if none.
    /// </summary>
    public string? GetDownloadUrl(int workItemId, int revisionIndex, string originalName)
    {
        _urls.TryGetValue(new AttachmentKey(workItemId, revisionIndex, originalName), out var url);
        return url;
    }

    private readonly record struct AttachmentKey(int WorkItemId, int RevisionIndex, string OriginalName);
}
