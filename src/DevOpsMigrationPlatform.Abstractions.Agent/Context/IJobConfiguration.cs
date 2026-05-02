// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Carries the per-job <see cref="IConfiguration"/> built from <c>migration-config.json</c>
/// for the currently active job. Set by the worker after reading the package config at job
/// start; cleared when the job completes or the lease is released.
///
/// Tools and connectors inject this and read <c>.PackageConfig?.GetSection(...)</c>
/// to obtain per-job options without depending on a compiled options model.
/// </summary>
public interface IJobConfiguration
{
    /// <summary>
    /// The raw <see cref="IConfiguration"/> built from <c>migration-config.json</c>.
    /// Tools use this to bind concrete endpoint types and per-job options sections.
    /// <c>null</c> when no job is active.
    /// Thread-safe: implementations must use volatile semantics.
    /// </summary>
    IConfiguration? PackageConfig { get; set; }

    /// <summary>Clears all state when a job completes or the lease is released.</summary>
    void Clear();
}
