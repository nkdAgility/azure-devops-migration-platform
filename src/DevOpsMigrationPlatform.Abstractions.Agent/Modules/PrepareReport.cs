// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

public enum PrepareIssueSeverity
{
    Warning = 0,
    Blocking = 1
}

public sealed record UnresolvedItem(
    string Key,
    string Reason,
    PrepareIssueSeverity Severity);

public sealed record PrepareReport
{
    public string ModuleName { get; init; } = string.Empty;
    public int ResolvedCount { get; init; }
    public int UnresolvedCount => UnresolvedItems.Count;
    public IReadOnlyList<UnresolvedItem> UnresolvedItems { get; init; } = [];
    public IReadOnlyList<ArtefactFinding> ArtefactFindings { get; init; } = [];
    public WorkItemsPrepareReadinessResult? Readiness { get; init; }
    public IReadOnlyList<ImportFailureFinding> FailureFindings { get; init; } = [];
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}

