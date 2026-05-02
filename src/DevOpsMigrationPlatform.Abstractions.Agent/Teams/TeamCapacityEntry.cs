// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A team member's capacity activities for a sprint.</summary>
public sealed record ActivityEntry(string Name, double CapacityPerDay);

/// <summary>Per-member capacity entry for a specific sprint.</summary>
public sealed record TeamCapacityEntry(
    string MemberDescriptor,
    string MemberDisplayName,
    IReadOnlyList<ActivityEntry> Activities,
    int DaysOff);
