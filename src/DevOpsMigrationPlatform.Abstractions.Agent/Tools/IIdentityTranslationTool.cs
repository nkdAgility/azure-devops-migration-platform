// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Single entry point for all identity resolution during import.
/// All modules that reference user identities MUST use this tool — no inline resolution.
/// </summary>
public interface IIdentityTranslationTool
{
    /// <summary>
    /// Whether the tool is enabled. When <c>false</c>, <see cref="Resolve"/> returns the source identity unchanged.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Loads identity descriptors and mapping overrides from the package boundary.
    /// Must be called once by <c>IdentitiesModule.ImportAsync</c> before any downstream module runs.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Resolves a source identity to a target identity.
    /// Resolution order: explicit override → default fallback → source identity pass-through.
    /// </summary>
    string Resolve(string sourceIdentity);

    /// <summary>
    /// Writes <c>Identities/unresolved.json</c> listing all source identities that had no explicit mapping.
    /// Call once at the end of <c>IdentitiesModule.ImportAsync</c>.
    /// </summary>
    Task WriteUnresolvedAsync(CancellationToken ct);
}
