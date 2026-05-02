// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Domain result of a WIQL query: an ordered list of matching work item IDs.
/// </summary>
/// <param name="WorkItemIds">Ordered list of matching work item IDs.</param>
public record WorkItemQueryResult(IReadOnlyList<int> WorkItemIds);
