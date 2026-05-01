using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Lease;

/// <summary>
/// Singleton that carries the per-job <see cref="IConfiguration"/> built from
/// <c>migration-config.json</c> for the currently active job.
/// Set by the worker after reading the package config at job start;
/// cleared when the job completes or the lease is released.
///
/// Tools and connectors inject this and read <c>.PackageConfig?.GetSection(...)</c>
/// to obtain per-job options without depending on a compiled <c>MigrationOptions</c> model.
/// </summary>
public sealed class ActiveJobConfigState
{
    private volatile IConfiguration? _packageConfig;

    /// <summary>
    /// The raw <see cref="IConfiguration"/> built from <c>migration-config.json</c>.
    /// Tools use this to bind concrete endpoint types and per-job options sections.
    /// <c>null</c> when no job is active.
    /// Thread-safe: volatile write/read.
    /// </summary>
    public IConfiguration? PackageConfig
    {
        get => _packageConfig;
        set => _packageConfig = value;
    }

    /// <summary>Clears all state when a job completes or the lease is released.</summary>
    public void Clear()
    {
        _packageConfig = null;
    }
}
