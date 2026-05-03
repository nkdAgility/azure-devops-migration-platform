// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Base extension options with only an Enabled flag.
/// </summary>
public class EnabledExtensionOptions
{
    /// <summary>Whether this extension is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;
}
