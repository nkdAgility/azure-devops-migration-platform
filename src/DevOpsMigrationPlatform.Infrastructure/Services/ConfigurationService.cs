#if !NET481
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

/// <summary>
/// JSON file-based implementation of <see cref="IConfigurationService"/>.
/// Loads, validates, and saves <see cref="MigrationOptions"/> from/to migration.json files.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly Dictionary<string, MigrationOptions> _configCache = new();

    private static readonly string[] DefaultConfigFileNames =
    {
        "migration.json",
        "migration.config.json",
        ".migration.json",
        "devops-migration.json"
    };

    public ConfigurationService(ILogger<ConfigurationService> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public async Task<MigrationOptions> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        var actualConfigPath = configPath ?? DiscoverDefaultConfigurationFile();

        if (actualConfigPath == null)
        {
            _logger.LogInformation("No configuration file specified or discovered. Using default configuration.");
            return new MigrationOptions();
        }

        var cacheKey = Path.GetFullPath(actualConfigPath);
        if (_configCache.TryGetValue(cacheKey, out var cachedConfig))
        {
            _logger.LogDebug("Returning cached configuration from {ConfigPath}", actualConfigPath);
            return cachedConfig;
        }

        try
        {
            _logger.LogInformation("Loading configuration from {ConfigPath}", actualConfigPath);

            if (!File.Exists(actualConfigPath))
                throw new FileNotFoundException($"Configuration file not found: {actualConfigPath}");

            var jsonContent = await File.ReadAllTextAsync(actualConfigPath, cancellationToken);
            var options = JsonSerializer.Deserialize<MigrationOptions>(jsonContent, _jsonOptions);

            if (options == null)
                throw new InvalidOperationException($"Failed to deserialize configuration from {actualConfigPath}");

            _configCache[cacheKey] = options;
            _logger.LogDebug("Successfully loaded configuration from {ConfigPath}", actualConfigPath);
            return options;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse configuration JSON from {ConfigPath}", actualConfigPath);
            throw new InvalidOperationException($"Invalid JSON in configuration file: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            _logger.LogError(ex, "Failed to load configuration from {ConfigPath}", actualConfigPath);
            throw new InvalidOperationException($"Failed to load configuration: {ex.Message}", ex);
        }
    }

    public IEnumerable<string> ValidateConfiguration(MigrationOptions options)
    {
        _logger.LogDebug("Validating configuration");
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Mode))
            errors.Add("Mode is required (Export, Import, or Both)");

        var mode = options.Mode?.Trim();
        var isExport = string.Equals(mode, "Export", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase);
        var isImport = string.Equals(mode, "Import", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "Both", StringComparison.OrdinalIgnoreCase);

        if (isExport && options.Source == null)
            errors.Add("Source configuration is required when Mode is Export or Both");

        if (isImport && options.Target == null)
            errors.Add("Target configuration is required when Mode is Import or Both");

        if (options.Source != null)
        {
            if (string.IsNullOrWhiteSpace(options.Source.Type))
                errors.Add("Source: Type is required");
            if (string.IsNullOrWhiteSpace(options.Source.OrgOrCollection))
                errors.Add("Source: OrgOrCollection is required");
        }

        if (options.Target != null)
        {
            if (string.IsNullOrWhiteSpace(options.Target.Type))
                errors.Add("Target: Type is required");
            if (string.IsNullOrWhiteSpace(options.Target.OrgOrCollection))
                errors.Add("Target: OrgOrCollection is required");
        }

        if (errors.Count > 0)
        {
            _logger.LogWarning("Configuration validation found {ErrorCount} errors", errors.Count);
            foreach (var error in errors)
                _logger.LogWarning("Configuration error: {Error}", error);
        }
        else
        {
            _logger.LogDebug("Configuration validation passed");
        }

        return errors;
    }

    public async Task SaveConfigurationAsync(MigrationOptions options, string configPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Saving configuration to {ConfigPath}", configPath);

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var jsonContent = JsonSerializer.Serialize(options, _jsonOptions);
            await File.WriteAllTextAsync(configPath, jsonContent, cancellationToken);

            var cacheKey = Path.GetFullPath(configPath);
            _configCache[cacheKey] = options;

            _logger.LogDebug("Successfully saved configuration to {ConfigPath}", configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to {ConfigPath}", configPath);
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    public IEnumerable<string> DiscoverConfigurationFiles()
    {
        var discoveredFiles = new List<string>();
        var currentDirectory = Directory.GetCurrentDirectory();

        _logger.LogDebug("Discovering configuration files starting from {CurrentDirectory}", currentDirectory);

        var searchDirectory = new DirectoryInfo(currentDirectory);
        while (searchDirectory != null)
        {
            foreach (var fileName in DefaultConfigFileNames)
            {
                var filePath = Path.Combine(searchDirectory.FullName, fileName);
                if (File.Exists(filePath))
                {
                    discoveredFiles.Add(filePath);
                    _logger.LogDebug("Discovered configuration file: {FilePath}", filePath);
                }
            }

            searchDirectory = searchDirectory.Parent;
            if (searchDirectory?.Parent == null)
                break;
        }

        return discoveredFiles;
    }

    private string? DiscoverDefaultConfigurationFile()
    {
        var discoveredFiles = DiscoverConfigurationFiles().ToList();
        if (discoveredFiles.Count > 0)
        {
            var selectedFile = discoveredFiles[0];
            _logger.LogDebug("Using discovered configuration file: {FilePath}", selectedFile);
            return selectedFile;
        }

        _logger.LogDebug("No configuration files discovered");
        return null;
    }
}
#endif
