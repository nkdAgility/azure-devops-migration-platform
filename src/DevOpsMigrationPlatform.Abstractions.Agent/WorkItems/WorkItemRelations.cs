// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// The current set of relations on a target work item, used for idempotency checks in Stage C.
/// </summary>
public record WorkItemRelations
{
    /// <summary>Related work item links currently on the target work item.</summary>
    public IReadOnlyList<RelatedWorkItemLink> RelatedLinks { get; init; } = Array.Empty<RelatedWorkItemLink>();

    /// <summary>External links currently on the target work item.</summary>
    public IReadOnlyList<ExternalWorkItemLink> ExternalLinks { get; init; } = Array.Empty<ExternalWorkItemLink>();

    /// <summary>Hyperlinks currently on the target work item.</summary>
    public IReadOnlyList<HyperlinkWorkItemLink> Hyperlinks { get; init; } = Array.Empty<HyperlinkWorkItemLink>();
}
