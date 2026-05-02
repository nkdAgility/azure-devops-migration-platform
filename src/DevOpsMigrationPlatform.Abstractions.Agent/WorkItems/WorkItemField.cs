// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// A single field value as it existed at a given work item revision.
/// </summary>
public record WorkItemField
{
    /// <summary>The field reference name, e.g. "System.Title".</summary>
    public string ReferenceName { get; init; } = string.Empty;

    /// <summary>The field value serialised to a string. Null if the field had no value.</summary>
    public string? Value { get; init; }
}
