using DevOpsMigrationPlatform.Abstractions.Jobs;
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Context passed to IModule.ExportAsync.
/// Provides the job definition and all required stores — modules must not access
/// the filesystem, source system, or target system directly.
/// </summary>
public class ExportContext
{
    public MigrationJob Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink ProgressSink { get; init; } = null!;
}
