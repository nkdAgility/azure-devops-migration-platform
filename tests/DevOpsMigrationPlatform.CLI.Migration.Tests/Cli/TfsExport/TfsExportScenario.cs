// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

/// <summary>
/// Fluent entry point for TFS export CLI behaviour tests.
/// Grouped by business capability: validation, progress visibility, fault handling.
/// </summary>
public sealed class TfsExportScenario
{
    private TfsExportScenario() { }

    public static TfsExportBuilder Arrange() => new();
}
