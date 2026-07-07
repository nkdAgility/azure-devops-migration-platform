// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// A single team field value entry — mirrors the REST <c>TeamFieldValue</c> shape
/// (<c>{ value, includeChildren }</c>) from the <c>teamsettings/teamfieldvalues</c>
/// contract. <paramref name="IncludeChildren"/> is <c>false</c> when the team has
/// "Exclude sub areas" set for this entry.
/// </summary>
public sealed record TeamFieldValueEntry(string Value, bool IncludeChildren = true);

/// <summary>
/// Area path (team field value) assignments for a team. Mirrors the Azure DevOps
/// <c>{project}/{team}/_apis/work/teamsettings/teamfieldvalues</c> REST contract
/// (api-version 7.1): <c>defaultValue</c> plus the full <c>values</c> list, each entry
/// carrying its own <c>includeChildren</c> flag. <see cref="FieldReferenceName"/> carries
/// <c>field.referenceName</c> — normally <see cref="AreaPathFieldReferenceName"/>, but
/// on-prem servers can be configured with a custom team field.
/// </summary>
[JsonConverter(typeof(TeamAreaPathsJsonConverter))]
public sealed record TeamAreaPaths(
    string DefaultValue,
    IReadOnlyList<TeamFieldValueEntry> Values,
    string FieldReferenceName = TeamAreaPaths.AreaPathFieldReferenceName)
{
    /// <summary>The standard team field reference name for area-path-based teams.</summary>
    public const string AreaPathFieldReferenceName = "System.AreaPath";

    /// <summary>
    /// True when the team field is the standard area path field — the only case where
    /// values participate in NodeTranslation area-path mapping.
    /// </summary>
    public bool IsAreaPathField
        => string.Equals(FieldReferenceName, AreaPathFieldReferenceName, StringComparison.OrdinalIgnoreCase);
}
