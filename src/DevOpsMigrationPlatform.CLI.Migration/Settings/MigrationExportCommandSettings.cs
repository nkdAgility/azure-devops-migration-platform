using System;
using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Settings for the export command.
/// All configuration (source URL, project, auth, modules) lives in the config file
/// specified by --config. See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationExportCommandSettings : MigrationCommandSettings
{
    private static readonly string[] ValidLevels =
        { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    [CommandOption("--follow")]
    [Description("Stream diagnostic log records to the console after job submission. Implicit in standalone mode.")]
    public bool Follow { get; init; }

    [CommandOption("--level")]
    [Description("Diagnostic log level for this job. Valid: Trace, Debug, Information, Warning, Error, Critical. Default: Information.")]
    public string Level { get; init; } = "Information";

    [CommandOption("--force-fresh")]
    [Description("Delete the export cursor and restart enumeration from the beginning. The identity map is preserved so no duplicate items are created.")]
    public bool ForceFresh { get; init; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful)
            return baseResult;

        if (Array.FindIndex(ValidLevels, v => v.Equals(Level, StringComparison.OrdinalIgnoreCase)) < 0)
            return ValidationResult.Error($"--level must be one of: {string.Join(", ", ValidLevels)}. Got: '{Level}'.");

        return ValidationResult.Success();
    }
}