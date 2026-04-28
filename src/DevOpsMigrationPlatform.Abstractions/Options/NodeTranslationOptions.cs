using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the NodeTranslation tool.
/// Bound from <c>MigrationPlatform:Tools:NodeTranslation</c>.
/// </summary>
public sealed class NodeTranslationOptions
{
    /// <summary>Configuration section path.</summary>
    public static string SectionName => "MigrationPlatform:Tools:NodeTranslation";

    /// <summary>
    /// Master switch. When <c>false</c>, all import-side tool behaviour is bypassed.
    /// Export-side artifacts are still written. Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Ordered regex mapping rules for area paths.
    /// Each entry has <c>Match</c> (regex pattern) and <c>Replacement</c> (regex replacement).
    /// First match wins. Case-insensitive.
    /// </summary>
    public IReadOnlyList<NodeMapping> AreaPathMappings { get; init; } = [];

    /// <summary>
    /// Ordered regex mapping rules for iteration paths.
    /// Same semantics as <see cref="AreaPathMappings"/>.
    /// </summary>
    public IReadOnlyList<NodeMapping> IterationPathMappings { get; init; } = [];

    /// <summary>
    /// When set, normalises the root segment of source area paths to this value
    /// before mapping rules are applied.
    /// </summary>
    public string? AreaLanguageOverride { get; init; }

    /// <summary>
    /// When set, normalises the root segment of source iteration paths to this value
    /// before mapping rules are applied.
    /// </summary>
    public string? IterationLanguageOverride { get; init; }

    /// <summary>
    /// When <c>true</c>, creates missing area/iteration nodes in the target via ADO API
    /// before the revision import loop.
    /// </summary>
    public bool AutoCreateNodes { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, skips revisions with unresolvable area paths (emits warning).
    /// When <c>false</c>, throws on unresolvable area path.
    /// </summary>
    public bool SkipOnUnresolvableArea { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, skips revisions with unresolvable iteration paths (emits warning).
    /// When <c>false</c>, throws on unresolvable iteration path.
    /// </summary>
    public bool SkipOnUnresolvableIteration { get; init; } = false;

    /// <summary>
    /// When <c>true</c>, import reads <c>Nodes/source-tree.json</c> and replicates all
    /// nodes to the target before processing revisions.
    /// </summary>
    public bool ReplicateSourceTree { get; init; } = false;
}
