// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A single team member with their admin flag.</summary>
public sealed record TeamMember(
    string Descriptor,
    string DisplayName,
    string UniqueName,
    bool IsAdmin);
