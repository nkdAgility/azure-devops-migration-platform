using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.CommandSettings;

/// <summary>
/// Settings for the validate command.
/// </summary>
public class ValidateCommandSettings : BaseCommandSettings
{
    [CommandArgument(0, "<package-path>")]
    [Description("Path to the migration package to validate")]
    public string? PackagePath { get; set; }

    [CommandOption("--schema-only")]
    [Description("Validate only package schema/structure")]
    public bool SchemaOnly { get; set; }
    
    [CommandOption("--work-items")]
    [Description("Validate work items data integrity")]
    [DefaultValue(true)]
    public bool ValidateWorkItems { get; set; } = true;
    
    [CommandOption("--attachments")]
    [Description("Validate attachment files are present and accessible")]
    [DefaultValue(true)]
    public bool ValidateAttachments { get; set; } = true;
    
    [CommandOption("--links")]
    [Description("Validate work item link references")]
    [DefaultValue(true)]
    public bool ValidateLinks { get; set; } = true;
    
    [CommandOption("--detailed")]
    [Description("Generate detailed validation report")]
    public bool DetailedReport { get; set; }
    
    [CommandOption("--output")]
    [Description("Output file for validation report")]
    public string? OutputFile { get; set; }
    
    [CommandOption("--format")]
    [Description("Report format: json, html, text")]
    [DefaultValue("text")]
    public string OutputFormat { get; set; } = "text";
}