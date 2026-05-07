// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class RuntimeStateTracingTests
{
    [TestMethod]
    public async Task RuntimeStateOperations_EmitExpectedSpanNames()
    {
        var spans = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "DevOpsMigrationPlatform.Migration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => spans.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(listener);

        _ = PackagePaths.CursorFile("export", "workitems", "https://dev.azure.com/contoso", "Shop");

        var stateStore = new Mock<IStateStore>(MockBehavior.Loose);
        var sut = new CheckpointingService(stateStore.Object);
        await sut.WriteCursorAsync("export.workitems", new CursorEntry { LastProcessed = "X", Stage = CursorStage.Completed }, CancellationToken.None);

        CollectionAssert.Contains(spans, "state.paths.resolve");
        CollectionAssert.Contains(spans, "state.cursor.update");
    }
}
