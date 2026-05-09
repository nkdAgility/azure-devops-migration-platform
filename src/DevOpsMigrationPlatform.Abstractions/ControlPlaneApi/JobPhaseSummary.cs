// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Summarises one ordered phase within a <see cref="JobTaskList"/> while preserving the
/// existing flat task list used by executors and legacy clients.
/// </summary>
public sealed record JobPhaseSummary
{
    /// <summary>Display name of the phase in canonical execution order.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Zero-based phase order within the plan.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Ordered task identifiers belonging to this phase. These reference entries in
    /// <see cref="JobTaskList.Tasks"/> and do not replace the flat task list.
    /// </summary>
    public IReadOnlyList<string> TaskIds { get; init; } = Array.Empty<string>();
}