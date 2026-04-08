namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

using System.ComponentModel;
using Spectre.Console.Cli;

/// <summary>
/// Base settings for any command that contacts the control plane.
/// Applies to: export, import, validate, migrate, prepare, manage *, tui.
/// Does NOT apply to discovery or configure commands.
/// </summary>
public class ControlPlaneBaseCommandSettings : BaseCommandSettings
{
    [CommandOption("--url")]
    [Description("URL of the control plane API. Overrides MIGRATION_API_URL. When neither is set the CLI starts the control plane in-process.")]
    public string? Url { get; init; }
}
