namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Job endpoint for the Simulated connector.
/// No credentials or server URL are required. The <see cref="Generator"/> property
/// carries the work item generation configuration (serialised as a <c>JsonElement</c>
/// at runtime). The consuming factory (<c>SimulatedWorkItemRevisionSourceFactory</c>)
/// deserialises it to <c>SimulatedGeneratorConfig</c>.
/// </summary>
public sealed class SimulatedJobEndpoint : JobEndpoint
{
    /// <summary>
    /// Generator configuration describing the work items to create.
    /// Serialised as JSON; typed as <c>object</c> because the concrete type
    /// (<c>SimulatedGeneratorConfig</c>) lives in <c>Infrastructure.Simulated</c>,
    /// which <c>Abstractions</c> must not reference.
    /// At runtime this is a <see cref="System.Text.Json.JsonElement"/> after
    /// JSON round-tripping through the control plane.
    /// </summary>
    public object? Generator { get; init; }
}
