namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Context passed to IDataTypeModule.ValidateAsync.
/// Validation is side-effect free — no writes to the package or target are permitted.
/// </summary>
public class ValidationContext
{
    public MigrationJob Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public System.Collections.Generic.List<ValidationError> Errors { get; } = new();
}
