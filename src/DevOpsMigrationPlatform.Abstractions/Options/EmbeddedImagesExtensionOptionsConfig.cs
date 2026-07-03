// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Embedded images data-aspect options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Data:EmbeddedImages</c> (ConfigVersion 2.0 anatomy).
/// </summary>
public sealed class EmbeddedImagesExtensionOptionsConfig : EnabledExtensionOptions
{
    /// <summary>Timeout in seconds for individual image downloads. Default: 30.</summary>
    public int DownloadTimeoutSeconds { get; init; } = 30;
}
