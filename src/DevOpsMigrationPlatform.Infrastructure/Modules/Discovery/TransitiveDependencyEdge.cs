
namespace DevOpsMigrationPlatform.Infrastructure.Modules.Discovery;

/// <summary>
/// A single edge in a transitive dependency graph, recording the depth at which
/// the link was discovered during BFS traversal.
/// </summary>
public readonly record struct TransitiveDependencyEdge
{
    public string SourceProject { get; init; }
    public string TargetProject { get; init; }
    public string TargetOrganisation { get; init; }
    public int LinkCount { get; init; }
    public LinkScope LinkScope { get; init; }
    public int Depth { get; init; }
    public bool IsCycleEdge { get; init; }
}
