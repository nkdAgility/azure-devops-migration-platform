using DevOpsMigrationPlatform.Abstractions.Options;
namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// The internal serialisable unit of execution handed from the CLI → Control Plane → Migration Agent.
/// Tool configuration travels in <c>migration-config.json</c> at the package root
/// (feature 025-agent-config-package). Schema v2.0.
/// See .agents/context/job-contract.md
/// </summary>
public class MigrationJob : Job
{
    /// <summary>Export, Import, or Both.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Source system type (e.g. "AzureDevOpsServices", "Simulated", "TeamFoundationServer").
    /// Populated by the CLI from <c>config.Source?.Type</c> at job construction time.
    /// Used by the control plane for capability-based agent routing.
    /// </summary>
    public string SourceType { get; init; } = string.Empty;

    /// <inheritdoc />
    /// Returns <c>null</c> when <see cref="SourceType"/> is empty so the job store
    /// treats it as "any agent can handle this" (e.g. import-only jobs with no source).
    public override string? GetSourceType() =>
        string.IsNullOrEmpty(this.SourceType) ? null : this.SourceType;
}

