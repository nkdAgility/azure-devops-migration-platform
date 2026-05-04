// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Generic counters for module phases (inventory/prepare).
/// </summary>
public record ModulePhaseCounters
{
    public long Attempted { get; init; }
    public long Completed { get; init; }
    public long Failed { get; init; }
    public long Unresolved { get; init; }
}

