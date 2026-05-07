// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class CursorUpdateTracingTests
{
    [TestMethod]
    public async Task WriteCursor_EmitsStateCursorUpdateSpan()
    {
        var names = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DevOpsMigrationPlatform.Migration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = a => names.Add(a.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var sut = new CheckpointingService(stateStore.Object);
        await sut.WriteCursorAsync("import.workitems", new CursorEntry { LastProcessed = "P", Stage = CursorStage.Completed }, CancellationToken.None);
        CollectionAssert.Contains(names, "state.cursor.update");
    }
}
