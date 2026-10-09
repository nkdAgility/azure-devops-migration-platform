// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DevOpsMigrationPlatform.Infrastructure.Storage;

/// <summary>
/// Builds the file path handed to SQLite as a connection <c>Data Source</c>.
/// </summary>
/// <remarks>
/// SQLite's native Windows file I/O honours MAX_PATH (260) unless the hosting process
/// declares <c>longPathAware</c> in its manifest — which the agent hosts do not — or the
/// path uses the <c>\\?\</c> extended-length form. SQLite also creates sidecar files next to
/// the database (<c>-journal</c>, <c>-wal</c>, <c>-shm</c>), so the extended form is applied
/// whenever the longest sidecar would reach MAX_PATH, not only when the database path does.
/// </remarks>
public static class SqliteFilePath
{
    private const int MaxPath = 260;

    /// <summary>Length of the longest sidecar suffix SQLite appends to a database path.</summary>
    private const int LongestSidecarSuffixLength = 8; // "-journal"

    private const string ExtendedPrefix = @"\\?\";
    private const string ExtendedUncPrefix = @"\\?\UNC\";

    /// <summary>
    /// Returns the full path of <paramref name="dbFilePath"/>, in extended-length form on Windows
    /// when the database or any of its sidecar files would reach MAX_PATH.
    /// </summary>
    public static string ToDataSource(string dbFilePath)
    {
        if (string.IsNullOrWhiteSpace(dbFilePath))
            throw new ArgumentException("Database file path must not be empty.", nameof(dbFilePath));

        if (dbFilePath.StartsWith(ExtendedPrefix, StringComparison.Ordinal))
            return dbFilePath;

        var fullPath = Path.GetFullPath(dbFilePath);
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            || fullPath.Length + LongestSidecarSuffixLength < MaxPath)
        {
            return fullPath;
        }

        return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            ? ExtendedUncPrefix + fullPath.Substring(2)
            : ExtendedPrefix + fullPath;
    }
}
