// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

// ── Legacy compatibility type (kept for OrganisationEntry and InventoryService until they are migrated) ──

/// <summary>
/// A scope entry used by inventory/organisation-level configuration.
/// Retained for backward compatibility with <see cref="OrganisationEntry.Scopes"/>.
/// </summary>
public sealed class MigrationPlatformOptionsScope
{
    /// <summary>Scope type identifier. Currently supported: <c>"wiql"</c>, <c>"filter"</c>.</summary>
    public string Type { get; init; } = string.Empty;

#if !NET481
    /// <summary>Scope-specific parameters.</summary>
    public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> Parameters { get; init; } = new();
#else
    public System.Collections.Generic.Dictionary<string, string> Parameters { get; init; } = new();
#endif
}
