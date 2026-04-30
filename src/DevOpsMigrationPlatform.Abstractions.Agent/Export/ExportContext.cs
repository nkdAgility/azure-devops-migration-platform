using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Context passed to IModule.ExportAsync.
/// Provides the job definition and all required stores — modules must not access
/// the filesystem, source system, or target system directly.
/// </summary>
public class ExportContext
{
    public Job Job { get; init; } = null!;
    public IArtefactStore ArtefactStore { get; init; } = null!;
    public IStateStore StateStore { get; init; } = null!;
    public IProgressSink ProgressSink { get; init; } = null!;
}

