using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Lease;

/// <summary>
/// Singleton that carries the agent's current <see cref="MigrationOptions"/> for the
/// active job. Set by the worker after reading <c>migration-config.json</c> from the
/// package at job start; cleared when the job completes or the lease is released.
///
/// Modules inject this and read <c>.Current?.Source</c>, <c>.Current?.Target</c>,
/// <c>.Current?.Policies</c>, <c>.Current?.Modules</c> instead of reading from
/// <see cref="DevOpsMigrationPlatform.Abstractions.Jobs.MigrationJob"/>.
/// </summary>
public sealed class ActiveJobConfigState
{
    private volatile MigrationOptions? _current;
    private volatile IConfiguration? _packageConfig;

    /// <summary>
    /// The <see cref="MigrationOptions"/> loaded from <c>migration-config.json</c>
    /// for the currently active job, or <c>null</c> if no job is active.
    /// Thread-safe: volatile write/read.
    /// </summary>
    public MigrationOptions? Current
    {
        get => _current;
        set => _current = value;
    }

    /// <summary>
    /// The raw <see cref="IConfiguration"/> built from <c>migration-config.json</c>.
    /// Agents use this to bind concrete endpoint types (e.g. <c>TeamFoundationServerEndpointOptions</c>)
    /// that cannot be populated via <c>IConfiguration.Bind</c> on abstract types.
    /// </summary>
    public IConfiguration? PackageConfig
    {
        get => _packageConfig;
        set => _packageConfig = value;
    }

    /// <summary>Clears all state when a job completes or the lease is released.</summary>
    public void Clear()
    {
        _current = null;
        _packageConfig = null;
    }
}
