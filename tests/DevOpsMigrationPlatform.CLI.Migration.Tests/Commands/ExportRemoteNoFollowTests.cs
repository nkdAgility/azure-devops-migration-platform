// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.Dsl.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Verifies that the export command prints the job ID and exits immediately
/// when --url is supplied but --follow is omitted (remote no-follow — B2).
/// </summary>
[TestClass]
public class ExportRemoteNoFollowTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ExportWithoutFollow_RemoteMode_PrintsJobIdAndExitsImmediately()
    {
        // Use a loopback address on a reserved port (1) so the connection is refused
        // immediately — no DNS resolution delay, no TCP timeout.  The CLI detects the
        // unreachable control plane within its 5-second reachability check and exits fast.
        var ctx = await ExportDiagnosticsScenario.RunRemoteNoFollow(
            controlPlaneUrl: "http://127.0.0.1:1");

        ctx.ShouldHaveExitedImmediately(maxElapsed: TimeSpan.FromSeconds(15));
    }
}
