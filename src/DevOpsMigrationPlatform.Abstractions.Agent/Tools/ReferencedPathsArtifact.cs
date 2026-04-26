using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Represents the <c>Nodes/referenced-paths.json</c> package artifact.
/// Contains the distinct area and iteration paths discovered during export.
/// </summary>
/// <param name="AreaPaths">Distinct area paths found in exported revisions.</param>
/// <param name="IterationPaths">Distinct iteration paths found in exported revisions.</param>
public sealed record ReferencedPathsArtifact(
    IReadOnlyList<string> AreaPaths,
    IReadOnlyList<string> IterationPaths);
