using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Options;

/// <summary>
/// Settings for the import command.
/// </summary>
public class ImportCommandSettings : BaseCommandSettings
{
    [CommandArgument(0, "<package-path>")]
    [Description("Path to the migration package to import")]
    public string? PackagePath { get; set; }
    
    [CommandArgument(1, "[target-url]")]
    [Description("Target system URL (e.g., https://dev.azure.com/organization)")]
    public string? TargetUrl { get; set; }

    [CommandOption("-p|--project")]
    [Description("Target project name (will be created if doesn't exist)")]
    public string? ProjectName { get; set; }
    
    [CommandOption("--validate-first")]
    [Description("Validate package before importing")]
    [DefaultValue(true)]
    public bool ValidateFirst { get; set; } = true;
    
    [CommandOption("--resume")]
    [Description("Resume a previous import from checkpoint")]
    public bool Resume { get; set; }
    
    [CommandOption("--checkpoint-path")]
    [Description("Path to checkpoint state file")]
    public string? CheckpointPath { get; set; }
    
    [CommandOption("--identity-mapping")]
    [Description("Path to identity mapping file")]
    public string? IdentityMappingFile { get; set; }
    
    [CommandOption("--work-items-only")]
    [Description("Import only work items (skip repos, pipelines, etc.)")]
    public bool WorkItemsOnly { get; set; }
    
    [CommandOption("--skip-validation")]
    [Description("Skip work item field validation during import")]
    public bool SkipValidation { get; set; }
    
    [CommandOption("--batch-size")]
    [Description("Number of work items to import in each batch")]
    [DefaultValue(50)]
    public int BatchSize { get; set; } = 50;
    
    [CommandOption("--auth-type")]
    [Description("Authentication type for target: PAT, Windows, OAuth")]
    public string? AuthType { get; set; }
    
    [CommandOption("--token")]
    [Description("Personal access token for target authentication")]
    public string? PersonalAccessToken { get; set; }
}