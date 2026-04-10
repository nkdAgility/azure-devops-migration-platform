using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Settings for the migrate command (export → validate → import in one run).
/// All configuration lives in the config file specified by --config.
/// See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationMigrateCommandSettings : MigrationCommandSettings
{
    [CommandOption("--force-fresh")]
    [Description("Delete all module cursors and the job phase record before running. The identity map is preserved so no duplicate items are created.")]
    public bool ForceFresh { get; init; }
}
