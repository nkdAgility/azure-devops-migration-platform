// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// Marker interface for command settings that require a migration scenario config file.
/// Commands whose settings implement this interface will trigger the interactive scenario
/// selector when <see cref="ConfigFile"/> is not supplied on the command line.
///
/// Control-plane observer commands (<c>tui</c>, <c>manage *</c>, <c>logs</c>) do NOT
/// implement this interface — they resolve the control-plane URL from
/// <c>EnvironmentOptions</c> and do not need a migration scenario config.
/// </summary>
public interface IRequiresMigrationConfig
{
    /// <summary>Path to the migration scenario configuration file.</summary>
    string? ConfigFile { get; }
}
