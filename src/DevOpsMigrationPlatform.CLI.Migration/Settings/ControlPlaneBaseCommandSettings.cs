// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Settings;

/// <summary>
/// Base settings for any command that contacts the control plane.
/// Applies to: queue, prepare, manage *, and tui.
/// Does NOT apply to config commands.
///
/// Control plane URL is resolved from the <c>MigrationPlatform:Environment:ControlPlane:BaseUrl</c>
/// configuration section via <see cref="Options.EnvironmentOptions"/>. The <c>--port</c> flag
/// overrides the port in standalone mode, allowing multiple concurrent local runs.
/// The <c>--url</c> flag switches to Hosted mode and sets the remote control plane URL.
/// </summary>
public class ControlPlaneBaseCommandSettings : BaseCommandSettings
{
    /// <summary>
    /// Port for the local control plane in standalone mode.
    /// When specified, overrides <c>ControlPlane.BaseUrl</c> to <c>http://localhost:{port}</c>.
    /// Default: <c>5100</c>.
    /// </summary>
    [CommandOption("--port")]
    [Description("Port for the local control plane. Overrides the default (5100) in standalone mode.")]
    [DefaultValue(5100)]
    public int Port { get; init; } = 5100;

    /// <summary>
    /// Remote control plane base URL. When supplied, switches the environment to Hosted mode
    /// and connects to this URL instead of starting a local control plane.
    /// Example: <c>https://migration.example.com</c>.
    /// </summary>
    [CommandOption("--url")]
    [Description("Remote control plane base URL. Switches to Hosted mode. Example: https://migration.example.com")]
    public string? Url { get; init; }
}
