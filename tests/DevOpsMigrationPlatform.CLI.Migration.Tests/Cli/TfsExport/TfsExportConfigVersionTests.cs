// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

/// <summary>
/// System tests for the ConfigVersion 2.0 hard cutover (ADR 0028, MC-H2):
/// a legacy v1 config must fail with an actionable rewrite message, and a
/// v2 Simulated config must run end-to-end.
/// </summary>
[TestClass]
public class TfsExportConfigVersionTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    public async Task Queue_WithV1Config_FailsWithActionableUpgradeMessage()
    {
        await using var result = await new TfsExportBuilder()
            .WithLegacyV1Config()
            .RunOutOfProcessAsync();

        Assert.AreNotEqual(0, result.ExitCode, "A ConfigVersion 1.0 file must be rejected.");
        var output = result.StandardError + result.StandardOutput;
        StringAssert.Contains(output, "no longer supported");
        StringAssert.Contains(output, "Rename 'Scope' to 'Selection'");
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    public async Task Queue_WithV2SimulatedConfig_ExportsEndToEnd()
    {
        await using var result = await new TfsExportBuilder()
            .WithSimulatedSource()
            .RunOutOfProcessAsync();

        Assert.AreEqual(0, result.ExitCode, result.StandardError);
    }
}
