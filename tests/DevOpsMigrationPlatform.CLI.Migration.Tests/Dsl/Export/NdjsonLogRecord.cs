// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;

/// <summary>
/// Minimal parsed representation of one NDJSON log record.
/// </summary>
public sealed class NdjsonLogRecord
{
    public string Level { get; init; } = string.Empty;     // e.g. "Information", "Debug"
    public string Message { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; }
}
