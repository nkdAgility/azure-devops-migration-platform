namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Determines whether a <see cref="WorkItemFilterOptions"/> includes or excludes matching work items.
/// </summary>
public enum FilterMode
{
    /// <summary>Work items matching the filter pattern are included.</summary>
    Include,

    /// <summary>Work items matching the filter pattern are excluded.</summary>
    Exclude,
}
