// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Canonical <see cref="IBoardConfigMergeTool"/> implementation (ADR-0024, EC-M4).
/// Pure and stateless — extracted from <c>BoardConfigTeamExtension</c> so the
/// merge/validation engine is a first-class seam; the extension keeps policy only.
/// </summary>
public sealed class BoardConfigMergeTool : IBoardConfigMergeTool
{
    /// <inheritdoc/>
    public IReadOnlyDictionary<string, ISet<string>> BuildValidStatesMap(
        IReadOnlyList<BoardColumn>? targetColumns)
    {
        var map = new Dictionary<string, ISet<string>>(StringComparer.OrdinalIgnoreCase);
        if (targetColumns is null) return map;
        foreach (var col in targetColumns)
        {
            foreach (var m in col.StateMappings ?? (IReadOnlyList<BoardColumnStateMapping>)Array.Empty<BoardColumnStateMapping>())
            {
                if (!map.TryGetValue(m.WorkItemType, out var states))
                    map[m.WorkItemType] = states = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                states.Add(m.State);
            }
        }
        return map;
    }

    /// <inheritdoc/>
    public BoardColumnValidationResult FilterInvalidStateMappings(
        IReadOnlyList<BoardColumn> columns,
        IReadOnlyDictionary<string, ISet<string>> validStates)
    {
        if (validStates.Count == 0)
            return new BoardColumnValidationResult(columns, Array.Empty<OmittedStateMapping>());

        var omitted = new List<OmittedStateMapping>();
        var result = new List<BoardColumn>(columns.Count);
        foreach (var col in columns)
        {
            var filtered = new List<BoardColumnStateMapping>();
            foreach (var m in col.StateMappings)
            {
                if (validStates.TryGetValue(m.WorkItemType, out var states) && states.Contains(m.State))
                {
                    filtered.Add(m);
                }
                else if (validStates.ContainsKey(m.WorkItemType))
                {
                    omitted.Add(new OmittedStateMapping(col.Name, m.WorkItemType, m.State));
                }
            }
            result.Add(col with { StateMappings = filtered });
        }
        return new BoardColumnValidationResult(result, omitted);
    }

    /// <inheritdoc/>
    public IReadOnlyList<T> MergeByName<T>(
        IReadOnlyList<T>? packageItems,
        IReadOnlyList<T>? targetItems,
        Func<T, string> keySelector)
    {
        if (packageItems is null or { Count: 0 } && targetItems is null or { Count: 0 })
            return Array.Empty<T>();
        if (targetItems is null or { Count: 0 }) return packageItems ?? Array.Empty<T>();
        if (packageItems is null or { Count: 0 }) return targetItems;

        var result = new List<T>(packageItems);
        var packageKeys = new HashSet<string>(packageItems.Select(keySelector), StringComparer.OrdinalIgnoreCase);
        foreach (var t in targetItems)
        {
            if (!packageKeys.Contains(keySelector(t)))
                result.Add(t);
        }
        return result;
    }
}
