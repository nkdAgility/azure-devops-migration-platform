namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

using System.ComponentModel;
using Spectre.Console.Cli;

/// <summary>
/// Settings for the <c>tui</c> command.
/// Extends <see cref="ControlPlaneBaseCommandSettings"/> with an optional job ID for direct jump.
/// </summary>
public sealed class TuiCommandSettings : ControlPlaneBaseCommandSettings
{
    [CommandOption("--job")]
    [Description("Skip the job list and pre-select this Job ID. Must be a valid GUID.")]
    public string? Job { get; init; }
}
