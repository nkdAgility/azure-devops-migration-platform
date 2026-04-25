using DevOpsMigrationPlatform.Abstractions.Jobs;
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Context passed to IModule.ImportAsync.
/// Import reads from the package via IArtefactStore and writes state via IStateStore.
/// </summary>
public class ImportContext
{
    public MigrationJob Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink ProgressSink { get; init; } = null!;
}
