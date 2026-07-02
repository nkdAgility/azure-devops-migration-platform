// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ProjectLifecycle;

/// <summary>
/// Canonical well-known process template identifiers shared by every connector
/// (ADR-0023 / VS-H3). Connectors resolve process names against this contract
/// instead of duplicating the mapping or reaching into another module.
/// </summary>
public static class KnownProcessIds
{
    public const string Agile = "adcc42ab-9882-485e-a3ed-7678f01f66bc";
    public const string Scrum = "6b724908-ef14-45cf-84f8-768b5384da45";
    public const string Cmmi = "27450541-8e31-4150-9947-dc59f998fc01";

    private static readonly Dictionary<string, string> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Agile"] = Agile,
        ["Scrum"] = Scrum,
        ["CMMI"] = Cmmi,
        ["Microsoft.VSTS.Process.CMMI"] = Cmmi,
        ["Microsoft.VSTS.Process.Scrum"] = Scrum,
        ["Microsoft.VSTS.Process.Agile"] = Agile
    };

    public static bool TryResolve(string? processName, out string processTypeId)
    {
        var normalizedName = (processName ?? string.Empty).Trim();
        if (normalizedName.Length > 0)
        {
            if (ByName.TryGetValue(normalizedName, out var knownTypeId))
            {
                processTypeId = knownTypeId;
                return true;
            }
        }

        processTypeId = Agile;
        return false;
    }
}
