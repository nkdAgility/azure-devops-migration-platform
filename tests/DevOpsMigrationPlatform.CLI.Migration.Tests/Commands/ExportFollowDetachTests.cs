// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Verifies that pressing Ctrl+C during a follow stream detaches without
/// cancelling the running job on the server (follow-detach — B3).
/// </summary>
[TestClass]
public class ExportFollowDetachTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task Follow_CtrlC_DetachesWithoutCancellingJob()
    {
        var ctx = await ExportDiagnosticsScenario.RunWithActiveFollowStream();

        ctx
            .ShouldHaveDetachedWithoutCancellingJob()
            .ShouldPrintTuiResumeHint();
    }
}
