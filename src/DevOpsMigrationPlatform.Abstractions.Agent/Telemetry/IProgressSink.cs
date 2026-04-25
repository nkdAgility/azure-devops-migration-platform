using DevOpsMigrationPlatform.Abstractions.Streaming;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Receives structured progress events emitted by the Job Engine, modules, and the TFS export agent.
/// The engine never writes to Console directly — all output goes through this interface.
/// </summary>
/// <remarks>
/// Implementations should throw on failure. When composed inside a
/// <c>CompositeProgressSink</c>, exceptions are caught, logged at Debug level,
/// and the remaining sinks still receive the event.
/// </remarks>
public interface IProgressSink
{
    void Emit(ProgressEvent evt);
}
