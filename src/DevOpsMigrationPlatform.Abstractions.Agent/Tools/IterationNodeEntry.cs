using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Represents a single iteration node entry as stored in <c>Nodes/source-tree.json</c>.
/// </summary>
/// <param name="Path">Full node path (e.g., <c>ProjectName\Sprint 1</c>).</param>
/// <param name="StartDate">Iteration start date. <c>null</c> for area nodes or undated iterations.</param>
/// <param name="FinishDate">Iteration finish date. <c>null</c> for area nodes or undated iterations.</param>
/// <param name="IsBacklogIteration"><c>true</c> if this is the backlog iteration for the project.</param>
public sealed record IterationNodeEntry(
    string Path,
    DateTimeOffset? StartDate,
    DateTimeOffset? FinishDate,
    bool IsBacklogIteration);
