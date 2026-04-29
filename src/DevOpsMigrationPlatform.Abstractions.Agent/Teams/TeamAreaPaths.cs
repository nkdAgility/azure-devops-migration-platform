using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Area path assignments for a team.</summary>
public sealed record TeamAreaPaths(
    string DefaultAreaPath,
    IReadOnlyList<string> IncludedAreaPaths);
