using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Settings for the import command.
/// All configuration (target URL, project, auth, modules) lives in the config file
/// specified by --config. See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationImportCommandSettings : MigrationCommandSettings
{
    [CommandOption("--force-fresh")]
    [Description("Delete the import cursor and restart enumeration from the beginning. The identity map is preserved so no duplicate items are created.")]
    public bool ForceFresh { get; init; }
}