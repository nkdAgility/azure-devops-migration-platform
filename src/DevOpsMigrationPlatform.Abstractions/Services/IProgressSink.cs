namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Receives structured progress events emitted by the Job Engine, modules, and the TFS export agent.
/// The engine never writes to Console directly — all output goes through this interface.
/// </summary>
public interface IProgressSink
{
    void Emit(ProgressEvent evt);
}
