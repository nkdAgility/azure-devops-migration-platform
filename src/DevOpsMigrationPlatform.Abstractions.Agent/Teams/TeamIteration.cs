using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A team's iteration (sprint) assignment.</summary>
public sealed record TeamIteration(
    string Id,
    string Path,
    string Name,
    DateTimeOffset? StartDate,
    DateTimeOffset? FinishDate,
    bool IsDefault,
    bool IsBacklog);
