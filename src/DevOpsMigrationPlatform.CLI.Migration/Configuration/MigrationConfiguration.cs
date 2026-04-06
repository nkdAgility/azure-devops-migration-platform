using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.CLI.Migration.Configuration;

/// <summary>
/// Root migration configuration loaded from migration.json or specified config file.
/// Provides centralized configuration model for all CLI operations.
/// </summary>
public class MigrationConfiguration
{
    /// <summary>
    /// Source system configuration (TFS, Azure DevOps, etc.)
    /// </summary>
    public SourceConfiguration? Source { get; set; }
    
    /// <summary>
    /// Target system configuration (Azure DevOps, etc.)
    /// </summary>
    public TargetConfiguration? Target { get; set; }
    
    /// <summary>
    /// Discovery and inventory specific settings.
    /// </summary>
    public InventoryConfiguration? Inventory { get; set; }
    
    /// <summary>
    /// Export operation settings.
    /// </summary>
    public ExportConfiguration? Export { get; set; }
    
    /// <summary>
    /// Import operation settings.
    /// </summary>
    public ImportConfiguration? Import { get; set; }
    
    /// <summary>
    /// Telemetry and logging configuration.
    /// </summary>
    public TelemetryConfiguration? Telemetry { get; set; }
    
    /// <summary>
    /// Validates the configuration and returns validation errors.
    /// </summary>
    /// <returns>Collection of validation errors, empty if valid</returns>
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();
        
        // Basic validation - at least source must be configured
        if (Source == null)
        {
            errors.Add("Source configuration is required");
        }
        
        // Validate sub-configurations if present
        if (Source != null)
        {
            errors.AddRange(Source.Validate().Select(e => $"Source: {e}"));
        }
        
        if (Target != null)
        {
            errors.AddRange(Target.Validate().Select(e => $"Target: {e}"));
        }
        
        if (Inventory != null)
        {
            errors.AddRange(Inventory.Validate().Select(e => $"Inventory: {e}"));
        }
        
        return errors;
    }
}

/// <summary>
/// Source system connection configuration.
/// </summary>
public class SourceConfiguration
{
    /// <summary>
    /// Source system type (TFS, AzureDevOpsServer, AzureDevOpsServices).
    /// </summary>
    [Required]
    public string? Type { get; set; }
    
    /// <summary>
    /// Connection URL for the source system.
    /// </summary>
    [Required]
    public string? Url { get; set; }
    
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    public AuthConfiguration? Authentication { get; set; }
    
    /// <summary>
    /// Project-specific settings.
    /// </summary>
    public ProjectConfiguration? Project { get; set; }
    
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Type))
            errors.Add("Type is required");
            
        if (string.IsNullOrWhiteSpace(Url))
            errors.Add("Url is required");
        else if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
            errors.Add("Url must be a valid URI");
            
        if (Authentication != null)
            errors.AddRange(Authentication.Validate());
            
        return errors;
    }
}

/// <summary>
/// Target system connection configuration.
/// </summary>
public class TargetConfiguration
{
    /// <summary>
    /// Target system type.
    /// </summary>
    [Required]
    public string? Type { get; set; }
    
    /// <summary>
    /// Connection URL for the target system.
    /// </summary>
    [Required]
    public string? Url { get; set; }
    
    /// <summary>
    /// Authentication configuration.
    /// </summary>
    public AuthConfiguration? Authentication { get; set; }
    
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Type))
            errors.Add("Type is required");
            
        if (string.IsNullOrWhiteSpace(Url))
            errors.Add("Url is required");
        else if (!Uri.TryCreate(Url, UriKind.Absolute, out _))
            errors.Add("Url must be a valid URI");
            
        return errors;
    }
}

/// <summary>
/// Authentication configuration for source/target connections.
/// </summary>
public class AuthConfiguration
{
    /// <summary>
    /// Authentication type (PAT, Windows, OAuth, etc.)
    /// </summary>
    public string? Type { get; set; }
    
    /// <summary>
    /// Personal Access Token (if using PAT auth).
    /// </summary>
    public string? PersonalAccessToken { get; set; }
    
    /// <summary>
    /// Username (if using basic auth).
    /// </summary>
    public string? Username { get; set; }
    
    /// <summary>
    /// Password (if using basic auth).
    /// </summary>
    public string? Password { get; set; }
    
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(Type))
            errors.Add("Authentication Type is required");
            
        // Validate based on auth type
        if (Type?.Equals("PAT", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (string.IsNullOrWhiteSpace(PersonalAccessToken))
                errors.Add("PersonalAccessToken is required for PAT authentication");
        }
        else if (Type?.Equals("Basic", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (string.IsNullOrWhiteSpace(Username))
                errors.Add("Username is required for Basic authentication");
            if (string.IsNullOrWhiteSpace(Password))
                errors.Add("Password is required for Basic authentication");
        }
        
        return errors;
    }
}

/// <summary>
/// Project-specific configuration.
/// </summary>
public class ProjectConfiguration
{
    /// <summary>
    /// Project name or ID.
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// Whether to include all projects or just specified ones.
    /// </summary>
    public bool AllProjects { get; set; }
    
    /// <summary>
    /// Specific project names to include (if not AllProjects).
    /// </summary>
    public List<string>? IncludedProjects { get; set; }
}

/// <summary>
/// Inventory operation configuration.
/// </summary>
public class InventoryConfiguration
{
    /// <summary>
    /// Output directory for inventory results.
    /// </summary>
    public string? OutputPath { get; set; }
    
    /// <summary>
    /// Whether to include all projects in inventory.
    /// </summary>
    public bool AllProjects { get; set; }
    
    /// <summary>
    /// Include work items in inventory.
    /// </summary>
    public bool IncludeWorkItems { get; set; } = true;
    
    /// <summary>
    /// Include repositories in inventory.
    /// </summary>
    public bool IncludeRepositories { get; set; } = true;
    
    /// <summary>
    /// Include pipelines in inventory.
    /// </summary>
    public bool IncludePipelines { get; set; } = true;
    
    public IEnumerable<string> Validate()
    {
        var errors = new List<string>();
        
        if (!string.IsNullOrWhiteSpace(OutputPath) && !Path.IsPathRooted(OutputPath))
        {
            // Allow relative paths, but validate they're valid
            try
            {
                Path.GetFullPath(OutputPath);
            }
            catch
            {
                errors.Add("OutputPath contains invalid characters");
            }
        }
        
        return errors;
    }
}

/// <summary>
/// Export operation configuration.
/// </summary>
public class ExportConfiguration
{
    /// <summary>
    /// Output directory for exported package.
    /// </summary>
    public string? OutputPath { get; set; }
    
    /// <summary>
    /// Whether to compress the export package.
    /// </summary>
    public bool Compress { get; set; } = true;
    
    /// <summary>
    /// Include attachments in export.
    /// </summary>
    public bool IncludeAttachments { get; set; } = true;
}

/// <summary>
/// Import operation configuration.
/// </summary>
public class ImportConfiguration
{
    /// <summary>
    /// Path to the package to import.
    /// </summary>
    public string? PackagePath { get; set; }
    
    /// <summary>
    /// Whether to validate package before import.
    /// </summary>
    public bool ValidateFirst { get; set; } = true;
    
    /// <summary>
    /// Dry run mode - validate only, don't import.
    /// </summary>
    public bool DryRun { get; set; } = false;
}

/// <summary>
/// Telemetry and logging configuration.
/// </summary>
public class TelemetryConfiguration
{
    /// <summary>
    /// Enable telemetry collection.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Log level for file logging.
    /// </summary>
    public string? LogLevel { get; set; } = "Information";
    
    /// <summary>
    /// Output directory for log files.
    /// </summary>
    public string? LogOutputPath { get; set; }
    
    /// <summary>
    /// Enable OpenTelemetry tracing.
    /// </summary>
    public bool EnableTracing { get; set; } = true;
    
    /// <summary>
    /// Enable OpenTelemetry metrics.
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
}