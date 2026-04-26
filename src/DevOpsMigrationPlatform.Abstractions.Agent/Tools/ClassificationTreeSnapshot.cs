using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Represents the <c>Nodes/source-tree.json</c> package artifact.
/// Holds the full source area and iteration classification tree captured during export.
/// </summary>
/// <param name="AreaNodes">Area node paths (plain strings).</param>
/// <param name="IterationNodes">Iteration nodes with optional dates and backlog flag.</param>
public sealed record ClassificationTreeSnapshot(
    IReadOnlyList<string> AreaNodes,
    IReadOnlyList<IterationNodeEntry> IterationNodes);
