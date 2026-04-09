namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Base settings for Migration commands (export, import, validate, migrate, prepare).
/// Inherits --url from <see cref="ControlPlaneBaseCommandSettings"/>.
/// All other configuration lives in the config file specified by --config.
/// See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationCommandSettings : ControlPlaneBaseCommandSettings
{
}