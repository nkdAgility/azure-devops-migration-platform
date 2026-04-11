using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

/// <summary>
/// Generic factory for creating work item comment sources with organization context.
/// Allows Infrastructure modules to create sources without direct references.
/// </summary>
public interface IWorkItemCommentSourceFactory
{
    /// <summary>
    /// Creates a comment source for the given ADO organization, project, and authentication.
    /// </summary>
    IWorkItemCommentSource Create(string organisationUrl, string project, string pat);
}
