#if !NETFRAMEWORK
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Writes ProgressEvent records to the migration package log file (Logs/progress.jsonl).
/// Placeholder implementation — full package writing is deferred to a future session.
/// </summary>
public sealed class PackageProgressSink : IProgressSink
{
    public void Emit(ProgressEvent evt)
    {
        // TODO: append NDJSON line to package Logs/progress.jsonl via IArtefactStore.
    }
}
#endif
