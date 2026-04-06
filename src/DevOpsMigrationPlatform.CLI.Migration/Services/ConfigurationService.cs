using System.Text.Json;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Service for loading and managing migration configuration.
/// Handles configuration file discovery, loading, validation, and caching.
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Loads configuration from the specified file path or discovers default configuration.
    /// </summary>
    /// <param name="configPath">Optional path to configuration file. If null, discovers default.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Loaded configuration</returns>
    Task<MigrationConfiguration> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates the provided configuration and returns validation errors.
    /// </summary>
    /// <param name="configuration">Configuration to validate</param>
    /// <returns>Collection of validation errors</returns>
    IEnumerable<string> ValidateConfiguration(MigrationConfiguration configuration);
    
    /// <summary>
    /// Saves configuration to the specified file path.
    /// </summary>
    /// <param name="configuration">Configuration to save</param>
    /// <param name="configPath">Path to save configuration to</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SaveConfigurationAsync(MigrationConfiguration configuration, string configPath, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discovers potential configuration files in the current directory and parent directories.
    /// </summary>
    /// <returns>Collection of discovered configuration file paths</returns>
    IEnumerable<string> DiscoverConfigurationFiles();
}

/// <summary>
/// Default implementation of configuration service.
/// </summary>
public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    
    // Cache loaded configuration to avoid repeated file reads
    private readonly Dictionary<string, MigrationConfiguration> _configCache = new();
    
    // Default configuration file names in order of preference
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
            WriteIndented = true
        };
    }
    
    public async Task<MigrationConfiguration> LoadConfigurationAsync(string? configPath = null, CancellationToken cancellationToken = default)
    {
        // Determine the configuration file path
        var actualConfigPath = configPath ?? DiscoverDefaultConfigurationFile();
        
        if (actualConfigPath == null)
        {
            _logger.LogInformation("No configuration file specified or discovered. Using default configuration.");
            return new MigrationConfiguration();
        }
        
        // Check cache first
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
            {
                throw new FileNotFoundException($"Configuration file not found: {actualConfigPath}");
            }
            
            var jsonContent = await File.ReadAllTextAsync(actualConfigPath, cancellationToken);
            var configuration = JsonSerializer.Deserialize<MigrationConfiguration>(jsonContent, _jsonOptions);
            
            if (configuration == null)
            {
                throw new InvalidOperationException($"Failed to deserialize configuration from {actualConfigPath}");
            }
            
            // Cache the loaded configuration
            _configCache[cacheKey] = configuration;
            
            _logger.LogDebug("Successfully loaded configuration from {ConfigPath}", actualConfigPath);
            return configuration;
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
    
    public IEnumerable<string> ValidateConfiguration(MigrationConfiguration configuration)
    {
        _logger.LogDebug("Validating configuration");
        
        var errors = configuration.Validate().ToList();
        
        if (errors.Any())
        {
            _logger.LogWarning("Configuration validation found {ErrorCount} errors", errors.Count);
            foreach (var error in errors)
            {
                _logger.LogWarning("Configuration error: {Error}", error);
            }
        }
        else
        {
            _logger.LogDebug("Configuration validation passed");
        }
        
        return errors;
    }
    
    public async Task SaveConfigurationAsync(MigrationConfiguration configuration, string configPath, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Saving configuration to {ConfigPath}", configPath);
            
            // Ensure directory exists
            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var jsonContent = JsonSerializer.Serialize(configuration, _jsonOptions);
            await File.WriteAllTextAsync(configPath, jsonContent, cancellationToken);
            
            // Update cache
            var cacheKey = Path.GetFullPath(configPath);
            _configCache[cacheKey] = configuration;
            
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
        
        // Search current directory and parent directories
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
            
            // Stop at root directory to avoid infinite loop
            if (searchDirectory?.Parent == null)
                break;
        }
        
        return discoveredFiles;
    }
    
    /// <summary>
    /// Discovers the default configuration file using the standard search pattern.
    /// </summary>
    /// <returns>Path to the first discovered configuration file, or null if none found</returns>
    private string? DiscoverDefaultConfigurationFile()
    {
        var discoveredFiles = DiscoverConfigurationFiles().ToList();
        
        if (discoveredFiles.Any())
        {
            var selectedFile = discoveredFiles.First();
            _logger.LogDebug("Using discovered configuration file: {FilePath}", selectedFile);
            return selectedFile;
        }
        
        _logger.LogDebug("No configuration files discovered");
        return null;
    }
}

/// <summary>
/// Configuration validation result with structured error information.
/// </summary>
public class ConfigurationValidationResult
{
    /// <summary>
    /// Whether the configuration is valid.
    /// </summary>
    public bool IsValid { get; init; }
    
    /// <summary>
    /// Collection of validation errors.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
    
    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>Valid result</returns>
    public static ConfigurationValidationResult Success() => new() { IsValid = true };
    
    /// <summary>
    /// Creates a failed validation result with errors.
    /// </summary>
    /// <param name="errors">Validation errors</param>
    /// <returns>Invalid result with errors</returns>
    public static ConfigurationValidationResult Failure(IEnumerable<string> errors) => 
        new() { IsValid = false, Errors = errors.ToList() };
}