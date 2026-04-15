namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Enumeration of work item link scope types for the dependency discovery feature.
/// Indicates whether the target of an external link is in a different project or organisation.
/// </summary>
public enum LinkScope
{
    /// <summary>
    /// The link target is in a different project within the same organisation.
    /// </summary>
    CrossProject,

    /// <summary>
    /// The link target is in a different organisation or collection.
    /// </summary>
    CrossOrganisation
}
