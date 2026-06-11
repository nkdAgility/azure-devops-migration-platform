// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Reflection;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

[TestClass]
public sealed class QueueFollowModeProgressTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ApplyTelemetry_MaintainsSteadyForwardMovement()
    {
        var queueType = typeof(QueueCommand);
        var stateType = queueType.GetNestedType("JobProgressState", BindingFlags.NonPublic)!;
        var initialFactory = stateType.GetMethod("Initial", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var applyTelemetry = queueType.GetMethod("ApplyTelemetry", BindingFlags.Static | BindingFlags.NonPublic)!;

        var state = initialFactory.Invoke(null, [100])!;
        state = applyTelemetry.Invoke(null, [state, new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Completed = 25, RevisionsProcessed = 100 }
            },
            Scope = new JobScopeCounters { WorkItemsTotal = 100 }
        }])!;

        var completedAfterFirst = (int)stateType.GetProperty("Completed")!.GetValue(state)!;

        state = applyTelemetry.Invoke(null, [state, new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Completed = 60, RevisionsProcessed = 220 }
            },
            Scope = new JobScopeCounters { WorkItemsTotal = 100 }
        }])!;

        var completedAfterSecond = (int)stateType.GetProperty("Completed")!.GetValue(state)!;
        Assert.IsTrue(completedAfterSecond >= completedAfterFirst);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ApplyTelemetry_DoesNotRegressCompletedOrRevisionCounts_WhenOlderSnapshotArrives()
    {
        var queueType = typeof(QueueCommand);
        var stateType = queueType.GetNestedType("JobProgressState", BindingFlags.NonPublic)!;
        var initialFactory = stateType.GetMethod("Initial", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var applyTelemetry = queueType.GetMethod("ApplyTelemetry", BindingFlags.Static | BindingFlags.NonPublic)!;

        var state = initialFactory.Invoke(null, [100])!;
        state = applyTelemetry.Invoke(null, [state, new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Completed = 60, RevisionsProcessed = 220 }
            },
            Scope = new JobScopeCounters { WorkItemsTotal = 100 }
        }])!;

        state = applyTelemetry.Invoke(null, [state, new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Completed = 25, RevisionsProcessed = 120 }
            },
            Scope = new JobScopeCounters { WorkItemsTotal = 100 }
        }])!;

        var completed = (int)stateType.GetProperty("Completed")!.GetValue(state)!;
        var revisions = (int)stateType.GetProperty("Revisions")!.GetValue(state)!;

        Assert.AreEqual(60, completed, "Completed count should stay monotonic when a stale telemetry snapshot is received.");
        Assert.AreEqual(220, revisions, "Revision count should stay monotonic when a stale telemetry snapshot is received.");
    }
}
