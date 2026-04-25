using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Generic factory for creating work item comment sources with organisation context.
/// Allows modules to create sources without direct references to infrastructure implementations.
/// </summary>
public interface IWorkItemCommentSourceFactory
{
    /// <summary>
    /// Creates a comment source for the given ADO organisation, project, and authentication.
    /// </summary>
    IWorkItemCommentSource Create(MigrationEndpointOptions endpoint, string project);
}
