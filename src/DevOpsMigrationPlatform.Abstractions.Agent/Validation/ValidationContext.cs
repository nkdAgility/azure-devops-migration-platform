using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Validation;

/// <summary>
/// Context passed to IModule.ValidateAsync.
/// Validation is side-effect free — no writes to the package or target are permitted.
/// </summary>
public class ValidationContext
{
    public Job Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public System.Collections.Generic.List<ValidationError> Errors { get; } = new();
}
