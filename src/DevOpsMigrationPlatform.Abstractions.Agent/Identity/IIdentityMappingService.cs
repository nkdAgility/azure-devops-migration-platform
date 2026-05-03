// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Identity;

/// <summary>
/// Resolves a source identity to a target identity.
/// All modules must use this service — no module may perform inline identity resolution.
/// </summary>
public interface IIdentityMappingService
{
    /// <summary>
    /// Returns the target identity for <paramref name="sourceIdentity"/>.
    /// Falls back to the configured default identity when no mapping exists,
    /// and records the unmapped identity for later warning output.
    /// </summary>
    string Resolve(string sourceIdentity);

    /// <summary>
    /// Loads explicit mapping overrides from the provided JSON string (contents of mapping.json).
    /// Implementations that do not support runtime override loading may treat this as a no-op.
    /// </summary>
    void LoadMappingOverrides(string? mappingJson);
}
