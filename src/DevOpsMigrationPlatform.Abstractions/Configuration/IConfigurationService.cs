using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Service for loading and managing migration configuration files.
/// Handles file discovery, loading, validation, and saving of the
/// <see cref="MigrationOptions"/> root configuration.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads configuration from the specified file path or discovers default configuration.
    /// </summary>
    Task<MigrationOptions> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the provided configuration and returns validation errors.
    /// </summary>
    IEnumerable<string> ValidateConfiguration(MigrationOptions options);

    /// <summary>
    /// Saves configuration to the specified file path.
    /// </summary>
    Task SaveConfigurationAsync(MigrationOptions options, string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers potential configuration files in the current directory and parent directories.
    /// </summary>
    IEnumerable<string> DiscoverConfigurationFiles();
}
