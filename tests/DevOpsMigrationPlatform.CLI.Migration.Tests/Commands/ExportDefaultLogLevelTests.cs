// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Verifies that the export command writes only Information-level records and above
/// when no --level flag is supplied (default log level floor — B1).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ExportDefaultLogLevelTests
{
    [TestCategory("CodeTest")]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportCommand_DefaultLevel_WritesOnlyInformationAndAbove()
    {
        var ctx = await ExportDiagnosticsScenario.RunWithDefaultLevel();

        PackageLogs
            .ReadDiagnosticsLog(ctx.OutputDirectory!)
            .ShouldContainOnlyLevelAndAbove("Information");
    }
}
