using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.CommandSettings;

/// <summary>
/// Settings for the inventory command.
/// </summary>
public class InventoryCommandSettings : BaseCommandSettings
{
    [CommandArgument(0, "[source-url]")]
    [Description("Source system URL (e.g., https://dev.azure.com/organization or http://tfs-server:8080/tfs/collection)")]
    public string? SourceUrl { get; set; }

    [CommandOption("-o|--output")]
    [Description("Output directory for inventory results")]
    public string? OutputPath { get; set; }
    
    [CommandOption("-a|--all-projects")]
    [Description("Include all projects in inventory")]
    public bool AllProjects { get; set; }
    
    [CommandOption("-p|--project")]
    [Description("Specific project name to inventory")]
    public string? ProjectName { get; set; }
    
    [CommandOption("--include-work-items")]
    [Description("Include work items in inventory (default: true)")]
    [DefaultValue(true)]
    public bool IncludeWorkItems { get; set; } = true;
    
    [CommandOption("--include-repos")]
    [Description("Include repositories in inventory (default: true)")]
    [DefaultValue(true)]
    public bool IncludeRepositories { get; set; } = true;
    
    [CommandOption("--include-pipelines")]
    [Description("Include build/release pipelines in inventory (default: true)")]
    [DefaultValue(true)]
    public bool IncludePipelines { get; set; } = true;
    
    [CommandOption("--include-teams")]
    [Description("Include team information in inventory (default: false)")]
    public bool IncludeTeams { get; set; }
    
    [CommandOption("--format")]
    [Description("Output format: json, csv, html")]
    [DefaultValue("json")]
    public string OutputFormat { get; set; } = "json";
    
    [CommandOption("--auth-type")]
    [Description("Authentication type: PAT, Windows, OAuth")]
    public string? AuthType { get; set; }
    
    [CommandOption("--token")]
    [Description("Personal access token for authentication")]
    public string? PersonalAccessToken { get; set; }
}