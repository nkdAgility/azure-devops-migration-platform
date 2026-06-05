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
    /// Whether the tool is enabled. When <c>false</c>, <see cref="Translate"/> returns the source identity unchanged.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// The configured default (fallback) target identity, or <c>null</c>/empty when none is configured.
    /// A <see cref="Translate"/> result equal to this value indicates the source identity was unresolved
    /// and fell back to the default — callers (e.g. team-member import) use this to skip rather than
    /// import under the wrong identity.
    /// </summary>
    string? DefaultIdentity { get; }

    /// <summary>
    /// Loads identity descriptors and mapping overrides from the package boundary.
    /// Must be called once by <c>IdentitiesModule.ImportAsync</c> before any downstream module runs.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Translates a source identity to a target identity (synchronous; reads cached results only).
    /// Resolution order: (1) explicit override → (2/3) cached Prepare-phase UPN/display-name match
    /// via <c>IIdentitiesOrchestrator.ResolvePrepared</c> → (4) configured default → source pass-through.
    /// </summary>
    string Translate(string sourceIdentity);

    /// <summary>
    /// Writes <c>Identities/unresolved.json</c> listing all source identities that had no explicit mapping.
    /// Call once at the end of <c>IdentitiesModule.ImportAsync</c>.
    /// </summary>
    Task WriteUnresolvedAsync(CancellationToken ct);
}
