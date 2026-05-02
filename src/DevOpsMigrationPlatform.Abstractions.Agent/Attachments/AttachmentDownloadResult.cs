// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// The outcome of a single attachment download attempt.
/// </summary>
public class AttachmentDownloadResult
{
    public bool Success { get; }
    public string? FilePath { get; }
    public Exception? Error { get; }

    private AttachmentDownloadResult(bool success, string? filePath, Exception? error)
    {
        Success = success;
        FilePath = filePath;
        Error = error;
    }

    public static AttachmentDownloadResult Succeeded(string filePath) =>
        new AttachmentDownloadResult(true, filePath, null);

    public static AttachmentDownloadResult Failed(Exception error) =>
        new AttachmentDownloadResult(false, null, error);
}
