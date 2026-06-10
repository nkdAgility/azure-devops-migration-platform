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
        var ctx = await ExportDiagnosticsScenario.RunRemoteNoFollow(
            controlPlaneUrl: "https://cp.example.com");

        ctx
            .ShouldPrintJobId()
            .ShouldHaveExitedImmediately(maxElapsed: TimeSpan.FromSeconds(30));
    }
}
