// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Per-organisation state within a <see cref="JobSnapshot"/>.
/// Contains the project-level breakdown for this organisation.
/// </summary>
public record OrgSnapshot
{
    /// <summary>Organisation URL (e.g. "https://dev.azure.com/contoso").</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Organisation display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Per-project snapshots within this organisation.</summary>
    public IReadOnlyList<ProjectSnapshot> Projects { get; init; } = [];
}
