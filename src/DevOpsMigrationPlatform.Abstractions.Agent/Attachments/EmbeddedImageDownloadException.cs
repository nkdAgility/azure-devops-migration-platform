// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// A source-hosted embedded image could not be downloaded because of a temporary failure
/// (throttling, a server error or a timeout) that persisted through the retries. It fails the
/// revision rather than letting export record it as done without the image, so the next run
/// retries it (Martin, 2026-11-17: temporary image failures must be retried).
/// </summary>
public sealed class EmbeddedImageDownloadException : Exception
{
    public EmbeddedImageDownloadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
