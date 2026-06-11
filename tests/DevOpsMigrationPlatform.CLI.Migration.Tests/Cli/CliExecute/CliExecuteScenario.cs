// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.CliExecute;

/// <summary>
/// Fluent entry point for CLI command execution safety tests.
/// Call <see cref="Arrange"/> to start a new scenario.
/// </summary>
public sealed class CliExecuteScenario
{
    private CliExecuteScenario() { }

    public static CliExecuteBuilder Arrange() => new();
}
