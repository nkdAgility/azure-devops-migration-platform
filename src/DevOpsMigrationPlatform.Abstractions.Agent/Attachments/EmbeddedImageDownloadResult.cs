// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// Result of downloading an embedded image from the source system.
/// Contains the image bytes, file extension, and size for storage.
/// </summary>
public record EmbeddedImageDownloadResult
{
    /// <summary>
    /// Raw image bytes downloaded from the source system.
    /// </summary>
    public byte[] Bytes { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// File extension (without leading dot), e.g., "png", "jpg", "gif".
    /// </summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Size in bytes of the downloaded image.
    /// </summary>
    public ulong Size { get; init; }
}
