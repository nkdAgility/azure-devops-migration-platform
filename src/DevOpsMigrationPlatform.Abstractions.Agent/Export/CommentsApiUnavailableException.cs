// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Thrown by an <see cref="IWorkItemCommentSource"/> when the source positively does not serve
/// the comments API (for example a server version without it). It is the only comment-read
/// failure the Comments extension skips; any other failure fails the revision so a resume
/// fetches it again (PR #222 review).
/// </summary>
public sealed class CommentsApiUnavailableException : Exception
{
    public CommentsApiUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
