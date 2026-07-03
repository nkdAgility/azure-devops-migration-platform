// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Config;

/// <summary>
/// JSON file-based implementation of <see cref="IConfigurationService"/>.
/// Loads, validates, and saves <see cref="MigrationPlatformOptions"/> from/to migration.json files.
/// Expects the JSON file to have a <c>MigrationPlatform</c> root section.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly Dictionary<string, MigrationPlatformOptions> _configCache = new();

    private static readonly string[] DefaultConfigFileNames =
    {
        "migration.json",
        "migration.config.json",
        ".migration.json",
        "devops-migration.json"
    };

    public ConfigurationService(
        ILogger<ConfigurationService> logger,
        PolymorphicEndpointOptionsConverter endpointConverter,
        PolymorphicOrganisationEntryConverter organisationConverter)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            Converters =
            {
                new JsonStringEnumConverter(),
                endpointConverter,
                organisationConverter
            }
        };
    }

    public async Task<MigrationPlatformOptions> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        var actualConfigPath = configPath ?? DiscoverDefaultConfigurationFile();

        if (actualConfigPath == null)
        {
            _logger.LogInformation("No configuration file specified or discovered. Using default configuration.");
            return new MigrationPlatformOptions();
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

            var jsonContent = await ReadAllTextCompatAsync(actualConfigPath, cancellationToken).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(jsonContent, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });

            if (!doc.RootElement.TryGetProperty("MigrationPlatform", out var platformElement))
                throw new InvalidOperationException(
                    $"Configuration error in '{actualConfigPath}': required 'MigrationPlatform' section is missing.");

            if (!platformElement.TryGetProperty("ConfigVersion", out var configVersionElement))
                throw new InvalidOperationException(
                    $"Configuration error in '{actualConfigPath}': 'MigrationPlatform.ConfigVersion' is required.");

            var configVersion = configVersionElement.GetString();
            if (!string.Equals(configVersion, "2.0", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Configuration error in '{actualConfigPath}': this file uses configuration version '{configVersion}', which is no longer supported. This release requires ConfigVersion '2.0'.\n" +
                    "Module options are now expressed as three aspects: 'Selection' (what to migrate), 'Data' (what to carry), 'Processing' (how to execute).\n" +
                    "To upgrade 'Modules.WorkItems':\n" +
                    "  1. Rename 'Scope' to 'Selection' ('Query' and 'Filters' are unchanged).\n" +
                    "  2. Move 'Extensions.Revisions', 'Extensions.Comments', and 'Extensions.EmbeddedImages' under 'Data'.\n" +
                    "  3. Move 'Extensions.WorkItemResolutionStrategy' under 'Processing'.\n" +
                    "  4. Delete the now-empty 'Extensions' object.\n" +
                    "  5. Set 'MigrationPlatform.ConfigVersion' to '2.0'.\n" +
                    "See docs/configuration-reference.md ('Module configuration anatomy') for the full v2 layout.");
            }

            // ConfigVersion says 2.0 — reject stray v1 module keys so nothing binds silently.
            // Teams v1 carried a top-level 'Scope' STRING; WorkItems v1 a 'Scope' OBJECT — both are legacy.
            if (platformElement.TryGetProperty("Modules", out var modulesElement)
                && modulesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var module in modulesElement.EnumerateObject())
                {
                    if (module.Value.ValueKind != JsonValueKind.Object) continue;
                    var legacyKeys = new List<string>();
                    if (module.Value.TryGetProperty("Scope", out var scopeEl)
                        && scopeEl.ValueKind is JsonValueKind.Object or JsonValueKind.String)
                        legacyKeys.Add("Scope");
                    if (module.Value.TryGetProperty("Extensions", out _))
                        legacyKeys.Add("Extensions");
                    if (legacyKeys.Count > 0)
                        throw new InvalidOperationException(
                            $"Configuration error in '{actualConfigPath}': 'Modules.{module.Name}' contains legacy key(s) " +
                            $"{string.Join(", ", legacyKeys.Select(k => $"'{k}'"))} which were removed in ConfigVersion 2.0. " +
                            "Rename 'Scope' to 'Selection'; move 'Extensions.Revisions'/'Comments'/'EmbeddedImages' under 'Data' " +
                            "and 'Extensions.WorkItemResolutionStrategy' under 'Processing'. See docs/configuration-reference.md.");
                }
            }

            var platformJson = platformElement.GetRawText();
            var options = JsonSerializer.Deserialize<MigrationPlatformOptions>(platformJson, _jsonOptions);

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
        catch (Exception ex) when (ex is not FileNotFoundException and not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to load configuration from {ConfigPath}", actualConfigPath);
            throw new InvalidOperationException($"Failed to load configuration: {ex.Message}", ex);
        }
    }

    public IEnumerable<string> ValidateConfiguration(MigrationPlatformOptions options)
    {
        _logger.LogDebug("Validating configuration");
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Mode))
            errors.Add("Mode is required (Inventory, Dependencies, Export, Prepare, Import, or Migrate)");

        var mode = options.Mode?.Trim();
        var isExport = string.Equals(mode, "Export", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "Migrate", StringComparison.OrdinalIgnoreCase);
        var isImport = string.Equals(mode, "Import", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "Prepare", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mode, "Migrate", StringComparison.OrdinalIgnoreCase);
        var isDiscovery = string.Equals(mode, "Inventory", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(mode, "Dependencies", StringComparison.OrdinalIgnoreCase);

        if (isExport && options.Source == null)
            errors.Add("Source configuration is required when Mode is Export or Migrate");

        if (isImport && options.Target == null)
            errors.Add("Target configuration is required when Mode is Prepare, Import, or Migrate");

        if (isDiscovery && options.Organisations.Count == 0)
            errors.Add("Organisations configuration is required when Mode is Inventory or Dependencies");

        if (options.Source != null)
        {
            if (string.IsNullOrWhiteSpace(options.Source.Type))
                errors.Add("Source: Type is required");
            else if (!IsValidSourceType(options.Source.Type))
                errors.Add($"Source: Type '{options.Source.Type}' is not supported. Valid values: AzureDevOpsServices, TeamFoundationServer, Simulated");

            options.Source.ValidateEndpointFields(errors, "Source");
        }

        if (options.Target != null)
        {
            if (string.IsNullOrWhiteSpace(options.Target.Type))
                errors.Add("Target: Type is required");
            else if (!IsValidTargetType(options.Target.Type))
                errors.Add($"Target: Type '{options.Target.Type}' is not supported. " +
                           "Only 'AzureDevOpsServices' or 'Simulated' are valid migration targets.");

            options.Target.ValidateEndpointFields(errors, "Target");
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

    private static bool IsValidSourceType(string type) =>
        string.Equals(type, "AzureDevOpsServices", StringComparison.Ordinal) ||
        string.Equals(type, "TeamFoundationServer", StringComparison.Ordinal) ||
        string.Equals(type, "Simulated", StringComparison.Ordinal);

    private static bool IsValidTargetType(string type) =>
        string.Equals(type, "AzureDevOpsServices", StringComparison.Ordinal) ||
        string.Equals(type, "Simulated", StringComparison.Ordinal);

    public async Task SaveConfigurationAsync(MigrationPlatformOptions options, string configPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Saving configuration to {ConfigPath}", configPath);

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            // Serialize MigrationPlatformOptions and wrap in { "MigrationPlatform": { "ConfigVersion": "2.0", ... } }
            var optionsJson = JsonSerializer.Serialize(options, _jsonOptions);
            var optionsNode = System.Text.Json.Nodes.JsonNode.Parse(optionsJson)!.AsObject();

            var platformNode = new System.Text.Json.Nodes.JsonObject();
            platformNode.Add("ConfigVersion", System.Text.Json.Nodes.JsonValue.Create("2.0"));
            foreach (var pair in optionsNode.ToList())
            {
                optionsNode.Remove(pair.Key);
                if (string.Equals(pair.Key, "ConfigVersion", StringComparison.OrdinalIgnoreCase))
                    continue; // canonical "2.0" already written above
                platformNode.Add(pair.Key, pair.Value);
            }

            var root = new System.Text.Json.Nodes.JsonObject { ["MigrationPlatform"] = platformNode };
            var writeOptions = new JsonSerializerOptions { WriteIndented = true };
            var jsonContent = root.ToJsonString(writeOptions);

            await WriteAllTextCompatAsync(configPath, jsonContent, cancellationToken).ConfigureAwait(false);

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

    private static async Task<string> ReadAllTextCompatAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private static async Task WriteAllTextCompatAsync(string path, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
