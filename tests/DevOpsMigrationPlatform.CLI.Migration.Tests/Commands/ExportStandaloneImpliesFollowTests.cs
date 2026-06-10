// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Verifies that standalone mode (no --url) implicitly activates follow mode
/// and streams diagnostics to the console (standalone implies follow — B4).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ExportStandaloneImpliesFollowTests
{
    [TestCategory("CodeTest")]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task StandaloneMode_ImpliesFollow_DiagnosticsStreamToConsole()
    {
        var ctx = await ExportDiagnosticsScenario.RunStandaloneNoUrl(level: "Warning");

        ctx
            .ShouldHaveStreamedDiagnosticsToConsole()
            .ShouldHaveExitedZero();
    }
}
