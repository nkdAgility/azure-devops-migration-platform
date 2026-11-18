// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// The provenance marker a resolution strategy adds to a work item it creates (#169): fields and
/// hyperlinks included in the one save that creates the item, so it can be found on a later run.
/// </summary>
public sealed record ImportProvenance(
    IReadOnlyList<WorkItemField> Fields,
    IReadOnlyList<HyperlinkWorkItemLink> Hyperlinks)
{
    /// <summary>No provenance marker.</summary>
    public static ImportProvenance Empty { get; } = new(Array.Empty<WorkItemField>(), Array.Empty<HyperlinkWorkItemLink>());
}
