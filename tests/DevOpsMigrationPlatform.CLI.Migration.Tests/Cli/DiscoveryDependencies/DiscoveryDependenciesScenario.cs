// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryDependencies;

/// <summary>
/// Fluent entry point for dependency-discovery CLI tests.
/// </summary>
public static class DiscoveryDependenciesScenario
{
    /// <summary>
    /// Begins a new dependency-discovery CLI scenario.
    /// </summary>
    public static DiscoveryDependenciesBuilder Arrange() => new();
}
