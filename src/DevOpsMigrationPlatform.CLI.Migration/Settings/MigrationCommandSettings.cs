using DevOpsMigrationPlatform.CLI.Migration.Commands;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Base settings for Migration commands (export, import, validate, migrate, prepare).
/// Inherits from <see cref="ControlPlaneBaseCommandSettings"/>.
/// Control plane URL is resolved from <c>MigrationPlatform:Environment:ControlPlane:BaseUrl</c>.
/// See docs/cli.md and .agents/context/cli-commands.md.
/// </summary>
public class MigrationCommandSettings : ControlPlaneBaseCommandSettings, IRequiresMigrationConfig
{
}