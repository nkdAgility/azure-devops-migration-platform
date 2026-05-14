// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class ActivePackageStateTests
{
    [TestMethod]
    public async Task CurrentRunId_WhenJobChanges_RecomputesRunContext()
    {
        var state = new ActivePackageState
        {
            CurrentJob = new Job { JobId = "job-one", Kind = JobKind.Export }
        };

        var firstRunId = state.CurrentRunId;
        await Task.Delay(1100);

        state.CurrentJob = new Job { JobId = "job-two", Kind = JobKind.Import };
        var secondRunId = state.CurrentRunId;

        Assert.IsFalse(string.IsNullOrWhiteSpace(firstRunId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(secondRunId));
        Assert.AreNotEqual(firstRunId, secondRunId, "Changing active job must reset cached run context.");
    }
}
