namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

using System.ComponentModel;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Spectre.Console.Cli;

/// <summary>
/// Settings for the export command.
/// All configuration (source URL, project, auth, modules) lives in the config file
/// specified by --config. See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationBaseCommandSettings : BaseCommandSettings
{
    [CommandOption("--url")]
    [Description("URL to the Control Plane service (e.g. https://domp-control-plane.azurewebsites.net) can be set via the environemnt variable MIGRATION_API_URL ")]
    public string? Url { get; set; }
}