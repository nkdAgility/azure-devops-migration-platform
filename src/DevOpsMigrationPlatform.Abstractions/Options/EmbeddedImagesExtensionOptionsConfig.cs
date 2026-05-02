// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Embedded images extension options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Extensions:EmbeddedImages</c>.
/// </summary>
public sealed class EmbeddedImagesExtensionOptionsConfig : EnabledExtensionOptions
{
    /// <summary>Timeout in seconds for individual image downloads. Default: 30.</summary>
    public int DownloadTimeoutSeconds { get; init; } = 30;
}
