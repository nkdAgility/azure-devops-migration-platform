// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Canonical ordering for log levels used by assertion extensions.
/// Matches the ordinal ordering in <c>QueueCommandSettings.ValidLevels</c>.
/// </summary>
internal static class LogLevelOrder
{
    private static readonly string[] Levels =
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public static int Rank(string level) =>
        Array.FindIndex(Levels, l => l.Equals(level, StringComparison.OrdinalIgnoreCase));
}
