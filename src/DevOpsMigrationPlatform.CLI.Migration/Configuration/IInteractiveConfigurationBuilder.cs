// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Migration.Configuration;

/// <summary>
/// Drives the interactive terminal prompts for configuring a migration and returns
/// the resulting <see cref="MigrationPlatformOptions"/>. Separated from the command class
/// so it can be tested without a real CLI host.
/// </summary>
internal interface IInteractiveConfigurationBuilder
{
    Task<MigrationPlatformOptions> BuildAsync(IAnsiConsole console, CancellationToken cancellationToken);
}
