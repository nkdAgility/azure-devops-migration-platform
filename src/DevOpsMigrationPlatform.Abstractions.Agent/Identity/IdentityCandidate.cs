// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Identity;

/// <summary>
/// A candidate target identity returned by an <see cref="IIdentityAdapter"/> query during
/// the Prepare phase. Immutable. <see cref="Descriptor"/> is the target-tenant descriptor
/// to which a matched source identity resolves.
/// </summary>
public sealed record IdentityCandidate(string Descriptor, string? Upn, string? DisplayName);
