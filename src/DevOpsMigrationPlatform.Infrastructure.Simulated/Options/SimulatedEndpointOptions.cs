using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

/// <summary>
/// Endpoint options for the Simulated connector. Requires no credentials and is used
/// for offline testing, unit tests, and CI scenarios where no real Azure DevOps instance
/// is available.
/// </summary>
public sealed class SimulatedEndpointOptions : MigrationEndpointOptions
{
    /// <summary>Base URL of the simulated target (defaults to a placeholder URL).</summary>
    public string? Url { get; init; }

    /// <summary>Project name (optional for simulated scenarios).</summary>
    public string? Project { get; init; }

    /// <summary>
    /// Generator configuration describing the work items to create.
    /// Required for export/source mode. May be omitted for import/target mode.
    /// </summary>
    public SimulatedGeneratorConfig Generator { get; init; } = new();

    /// <inheritdoc/>
    public override string GetEndpointUrl() => Url ?? "https://simulated.example.com";

    /// <inheritdoc/>
    public override string GetProject() => Project ?? "SimulatedProject";

    /// <inheritdoc/>
    public override string GetResolvedUrl() => Url ?? "https://simulated.example.com";

    /// <inheritdoc/>
    public override void ValidateEndpointFields(List<string> errors, string role)
    {
        // No required fields for simulated endpoints.
    }
}
