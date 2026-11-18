// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;

/// <summary>
/// Runs a WIQL query on a TFS target through the Object Model and streams each matching work
/// item's id with one field's value. The Object Model serves every TFS version back to TFS 2008,
/// which has no REST API (Martin, 2026-11-18: build the TFS agent as if there may be no REST API).
/// </summary>
public interface ITfsWorkItemQuery
{
    IEnumerable<(int Id, string? Value)> Query(string wiql, string fieldName);
}
