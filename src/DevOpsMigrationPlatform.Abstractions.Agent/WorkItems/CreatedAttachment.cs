// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// An attachment the target stored while creating a work item from one revision (#169). The
/// import saves it as the attachment's mapping, so a resumed revision reuses it instead of
/// uploading the binary again.
/// </summary>
/// <param name="RelativePath">The attachment's package path, from <see cref="PendingAttachment.RelativePath"/>.</param>
/// <param name="AttachmentId">The target's identifier for the stored attachment (the value <c>UploadAttachmentAsync</c> returns).</param>
public sealed record CreatedAttachment(string RelativePath, string AttachmentId);
