using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// Configuration for the <see cref="DiagnosticLogStore"/> ring buffer.
/// Bound from the <c>DiagnosticLog</c> configuration section.
/// </summary>
public sealed class DiagnosticLogStoreOptions
{
    public const string SectionName = "DiagnosticLog";

    /// <summary>Maximum number of records retained per job in the ring buffer.</summary>
    [Range(1, 100_000)]
    public int Capacity { get; init; } = 1000;

    /// <summary>
    /// Deployment-level minimum log level for the control plane.
    /// Records below this level are discarded before buffering.
    /// Default: <c>"Warning"</c>. In standalone mode, set to the operator's <c>--level</c>.
    /// </summary>
    public string MinimumLevel { get; init; } = "Warning";
}
