// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Identity;

/// <summary>
/// Descriptor for a single user or group identity exported from the source system.
/// Immutable record — serialised to JSONL in <c>Identities/descriptors.jsonl</c>.
/// </summary>
public sealed record IdentityDescriptor(
    string Descriptor,
    string DisplayName,
    string UniqueName,
    string SourceType,
    string Origin,
    bool IsActive);
