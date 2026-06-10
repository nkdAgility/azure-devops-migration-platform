// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Well-known dotnet test --filter expressions for system test category control.
/// </summary>
public static class TestRunFilter
{
    /// <summary>Runs only tests carrying [TestCategory("SystemTest")].</summary>
    public const string SystemTestOnly = "TestCategory=SystemTest";

    /// <summary>Excludes all tests carrying [TestCategory("SystemTest")].</summary>
    public const string ExcludeSystemTests = "TestCategory!=SystemTest";

    /// <summary>Runs only tests carrying [TestCategory("UnitTests")] — fastest, no I/O, no integration.</summary>
    public const string UnitTestsOnly = "TestCategory=UnitTests";
}
