namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Identifies whether a classification node is an area node or an iteration node.</summary>
public enum ClassificationNodeType
{
    /// <summary>An area node (System.AreaPath).</summary>
    Area,

    /// <summary>An iteration node (System.IterationPath).</summary>
    Iteration
}
