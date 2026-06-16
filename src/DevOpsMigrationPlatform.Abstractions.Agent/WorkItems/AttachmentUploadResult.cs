// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Result of uploading an attachment binary to the target system.
/// Carries the URL returned by the binary-upload call along with the
/// display name and optional comment needed for the relation add.
/// </summary>
public sealed record AttachmentUploadResult(
    string AttachmentUrl,
    string FileName,
    string? Comment = null);
