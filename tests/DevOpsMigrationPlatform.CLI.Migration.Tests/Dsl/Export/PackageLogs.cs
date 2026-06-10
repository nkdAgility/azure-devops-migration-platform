// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Reads structured log files written by the Migration Agent and the CLI
/// follow-mode streaming into the package output directory.
/// </summary>
public static class PackageLogs
{
    /// <summary>
    /// Reads all NDJSON records from the agent log file
    /// (<c>agent.jsonl</c>) found anywhere under <paramref name="outputDir"/>.
    /// Returns an empty list (not null) when no file is present.
    /// </summary>
    public static IReadOnlyList<NdjsonLogRecord> ReadAgentLog(string outputDir)
        => ReadFirstMatchingNdjson(outputDir, "agent.jsonl");

    /// <summary>
    /// Reads all NDJSON records from the diagnostics log file
    /// (<c>diagnostics.ndjson</c>) found anywhere under <paramref name="outputDir"/>.
    /// Returns an empty list (not null) when no file is present.
    /// </summary>
    public static IReadOnlyList<NdjsonLogRecord> ReadDiagnosticsLog(string outputDir)
        => ReadFirstMatchingNdjson(outputDir, "diagnostics.ndjson");

    private static IReadOnlyList<NdjsonLogRecord> ReadFirstMatchingNdjson(
        string outputDir, string fileName)
    {
        if (!Directory.Exists(outputDir))
            return Array.Empty<NdjsonLogRecord>();

        var matches = Directory.GetFiles(outputDir, fileName, SearchOption.AllDirectories);
        if (matches.Length == 0)
            return Array.Empty<NdjsonLogRecord>();

        return ParseNdjsonFile(matches[0]);
    }

    private static IReadOnlyList<NdjsonLogRecord> ParseNdjsonFile(string path)
    {
        var records = new List<NdjsonLogRecord>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                var level = root.TryGetProperty("level", out var lv) ? lv.GetString() ?? string.Empty
                          : root.TryGetProperty("Level", out var lv2) ? lv2.GetString() ?? string.Empty
                          : string.Empty;

                var message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? string.Empty
                            : root.TryGetProperty("Message", out var msg2) ? msg2.GetString() ?? string.Empty
                            : string.Empty;

                var timestamp = DateTimeOffset.UtcNow;
                if (root.TryGetProperty("timestamp", out var ts) || root.TryGetProperty("Timestamp", out ts))
                    DateTimeOffset.TryParse(ts.GetString(), out timestamp);

                records.Add(new NdjsonLogRecord
                {
                    Level = level,
                    Message = message,
                    Timestamp = timestamp,
                });
            }
            catch (JsonException)
            {
                // Skip malformed lines — log files may be partially written.
            }
        }

        return records;
    }
}
