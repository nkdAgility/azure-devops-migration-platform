// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Holds the raw package configuration for the currently executing job.
/// This is a transition seam for option binding while ambient <see cref="IJobConfiguration"/>
/// is being retired from tool registrations.
/// </summary>
public interface ICurrentPackageConfigAccessor
{
    /// <summary>The current package configuration, or <see langword="null"/> when unavailable.</summary>
    IConfiguration? Current { get; }

    /// <summary>Sets the current package configuration.</summary>
    void Set(IConfiguration configuration);

    /// <summary>Clears the current package configuration.</summary>
    void Clear();
}