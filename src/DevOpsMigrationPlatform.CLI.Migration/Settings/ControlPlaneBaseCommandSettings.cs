namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Base settings for any command that contacts the control plane.
/// Applies to: export, import, validate, migrate, prepare, manage *, tui.
/// Does NOT apply to discovery or configure commands.
///
/// Control plane URL is resolved from the <c>MigrationPlatform:Environment:ControlPlane:BaseUrl</c>
/// configuration section via <see cref="Options.EnvironmentOptions"/>. No CLI override is provided;
/// the config file is the single source of truth.
/// </summary>
public class ControlPlaneBaseCommandSettings : BaseCommandSettings
{
}
