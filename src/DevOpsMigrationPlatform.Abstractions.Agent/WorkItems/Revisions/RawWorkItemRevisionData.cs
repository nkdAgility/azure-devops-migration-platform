// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems.Revisions;

/// <summary>
/// SDK-free representation of a single Azure DevOps work item revision as returned by
/// the REST API. Used as the input type for <c>IAzureDevOpsWorkItemRevisionMapper.Map</c>
/// so that the mapping interface carries no SDK dependency.
/// </summary>
public sealed record RawWorkItemRevisionData
{
    public int? Id { get; init; }
    public int? Rev { get; init; }
    public IReadOnlyDictionary<string, object?> Fields { get; init; } = new Dictionary<string, object?>();
    public IReadOnlyList<RawWorkItemRelation> Relations { get; init; } = [];
}

/// <summary>
/// SDK-free representation of a work item relation within a <see cref="RawWorkItemRevisionData"/>.
/// </summary>
public sealed record RawWorkItemRelation
{
    public string? Rel { get; init; }
    public string? Url { get; init; }
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = new Dictionary<string, object?>();
}
