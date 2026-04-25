using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

/// <summary>
/// Root generator configuration for the Simulated connector.
/// Specifies the projects and work item types to generate.
/// An empty <see cref="Projects"/> list is valid — the job completes immediately with zero items.
/// </summary>
public sealed class SimulatedGeneratorConfig
{
    /// <summary>Projects to generate. May be empty.</summary>
    public List<SimulatedProjectConfig> Projects { get; set; } = new();
}
