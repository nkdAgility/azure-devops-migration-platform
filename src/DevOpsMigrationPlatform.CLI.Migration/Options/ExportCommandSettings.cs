using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Options;

/// <summary>
/// Settings for the export command.
/// </summary>
public class ExportCommandSettings : BaseCommandSettings
{
    [CommandArgument(0, "[source-url]")]
    [Description("Source system URL (e.g., https://dev.azure.com/organization or http://tfs-server:8080/tfs/collection)")]
    public string? SourceUrl { get; set; }

    [CommandOption("-o|--output")]
    [Description("Output directory for export package")]
    public string? OutputPath { get; set; }
    
    [CommandOption("-p|--project")]
    [Description("Specific project name to export")]
    public string? ProjectName { get; set; }
    
    [CommandOption("--all-projects")]
    [Description("Export all projects")]
    public bool AllProjects { get; set; }
    
    [CommandOption("--compress")]
    [Description("Compress the export package")]
    [DefaultValue(true)]
    public bool Compress { get; set; } = true;
    
    [CommandOption("--include-attachments")]
    [Description("Include work item attachments in export")]
    [DefaultValue(true)]
    public bool IncludeAttachments { get; set; } = true;
    
    [CommandOption("--include-history")]
    [Description("Include work item history/revisions")]
    [DefaultValue(true)]
    public bool IncludeHistory { get; set; } = true;
    
    [CommandOption("--include-repos")]
    [Description("Include git repositories in export")]
    public bool IncludeRepositories { get; set; }
    
    [CommandOption("--include-pipelines")]
    [Description("Include build/release pipelines in export")]
    public bool IncludePipelines { get; set; }
    
    [CommandOption("--work-item-types")]
    [Description("Comma-separated list of work item types to export (default: all)")]
    public string? WorkItemTypes { get; set; }
    
    [CommandOption("--from-date")]
    [Description("Export items changed after this date (yyyy-MM-dd)")]
    public DateOnly? FromDate { get; set; }
    
    [CommandOption("--to-date")]
    [Description("Export items changed before this date (yyyy-MM-dd)")]
    public DateOnly? ToDate { get; set; }
    
    [CommandOption("--auth-type")]
    [Description("Authentication type: PAT, Windows, OAuth")]
    public string? AuthType { get; set; }
    
    [CommandOption("--token")]
    [Description("Personal access token for authentication")]
    public string? PersonalAccessToken { get; set; }
}