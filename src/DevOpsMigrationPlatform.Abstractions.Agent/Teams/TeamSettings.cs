#if !NET481
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Team board/backlog configuration settings.</summary>
public sealed record TeamSettings(
    string BacklogNavigationLevel,
    bool BugsBehavior,
    IReadOnlyList<string> WorkingDays);
#endif
