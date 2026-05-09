// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Terminal task outcome reported by an executee back to the scheduler.
/// </summary>
public sealed record TaskExecutionResult(
    JobTaskStatus Status,
    string? StatusMessage = null,
    long? KnownTotal = null,
    long? CompletedCount = null)
{
    public static TaskExecutionResult Completed(long? knownTotal = null, long? completedCount = null)
        => new(JobTaskStatus.Completed, KnownTotal: knownTotal, CompletedCount: completedCount);

    public static TaskExecutionResult Skipped(string reason, long? knownTotal = null, long? completedCount = null)
        => new(JobTaskStatus.Skipped, reason, KnownTotal: knownTotal, CompletedCount: completedCount);
}