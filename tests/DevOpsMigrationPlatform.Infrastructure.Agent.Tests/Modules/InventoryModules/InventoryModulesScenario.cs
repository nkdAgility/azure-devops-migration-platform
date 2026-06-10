// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules.InventoryModules;

/// <summary>
/// Fluent entry point for inventory-modules behaviour tests.
/// Call <see cref="Arrange"/> to start a new scenario.
/// </summary>
public sealed class InventoryModulesScenario
{
    private InventoryModulesScenario() { }

    /// <summary>Starts a new scenario arrangement.</summary>
    public static InventoryModulesBuilder Arrange() => new();
}
