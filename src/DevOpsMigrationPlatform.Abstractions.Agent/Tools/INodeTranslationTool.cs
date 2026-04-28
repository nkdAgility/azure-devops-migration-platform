namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Translates area and iteration path values from source to target format.
/// Pure transformation — no I/O, no state. Stateless — safe to call concurrently.
/// </summary>
public interface INodeTranslationTool
{
    /// <summary>
    /// Translates a single path value using language override, regex mapping rules,
    /// and auto project-name swap. Pure transformation — no I/O.
    /// </summary>
    /// <param name="fieldName"><c>System.AreaPath</c> or <c>System.IterationPath</c>.</param>
    /// <param name="sourcePathValue">The raw path from revision.json.</param>
    /// <param name="context">Source/target project names.</param>
    /// <returns>Translation result with metadata.</returns>
    PathTranslation TranslatePath(
        string fieldName,
        string sourcePathValue,
        ProjectMapping context);

    /// <summary>Whether the tool is enabled (Enabled config flag).</summary>
    bool IsEnabled { get; }
}
