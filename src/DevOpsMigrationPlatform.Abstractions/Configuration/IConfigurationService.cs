// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Configuration;

/// <summary>
/// Service for loading and managing migration configuration files.
/// Handles file discovery, loading, validation, and saving of the
/// <see cref="MigrationPlatformOptions"/> root configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads configuration from the specified file path or discovers default configuration.
    /// </summary>
    Task<MigrationPlatformOptions> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the provided configuration and returns validation errors.
    /// </summary>
    IEnumerable<string> ValidateConfiguration(MigrationPlatformOptions options);

    /// <summary>
    /// Saves configuration to the specified file path.
    /// </summary>
    Task SaveConfigurationAsync(MigrationPlatformOptions options, string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers potential configuration files in the current directory and parent directories.
    /// </summary>
    IEnumerable<string> DiscoverConfigurationFiles();
}
