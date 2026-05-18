// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// A no-op <see cref="IIdentityMappingService"/> that returns the source identity unchanged.
/// Used during import when no identity mapping file is configured.
/// Full identity resolution is added in US4/T031.
/// </summary>
public sealed class PassThroughIdentityMappingService : IIdentityMappingService
{
    /// <inheritdoc/>
    public string Resolve(string sourceIdentity) => sourceIdentity;

    /// <inheritdoc/>
    public void LoadMappingOverrides(string? mappingJson) { }
}
