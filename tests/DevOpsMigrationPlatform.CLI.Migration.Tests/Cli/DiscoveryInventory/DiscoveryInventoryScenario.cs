// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryInventory;

/// <summary>
/// Fluent entry point for Discovery Inventory CLI behaviour tests.
/// Call <see cref="Arrange"/> to start a new scenario.
/// </summary>
public sealed class DiscoveryInventoryScenario
{
    private DiscoveryInventoryScenario() { }

    public static DiscoveryInventoryBuilder Arrange() => new();
}
