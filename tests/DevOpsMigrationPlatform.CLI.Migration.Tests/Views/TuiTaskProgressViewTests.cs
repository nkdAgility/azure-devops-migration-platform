// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Views;

[TestClass]
public sealed class TuiTaskProgressViewTests
{
    [TestMethod]
    public void BuildMigrationWorkspace_WhenTaskListHasExplicitPhaseSummaries_UsesSummaryPhases()
    {
        var buildMigrationWorkspace = typeof(TuiTaskProgressView).GetMethod(
            "BuildMigrationWorkspace",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(buildMigrationWorkspace, "BuildMigrationWorkspace method was not found.");

        var summary = new JobSummary(
            Guid.NewGuid(),
            "Prepare",
            "Running",
            "tester@example.com",
            DateTimeOffset.UtcNow);

        var taskList = new JobTaskList
        {
            Tasks = new List<JobTask>
            {
                new()
                {
                    Id = "analyse.inventory.org.project",
                    Name = "Inventory Analyse",
                    TaskKind = TaskKind.Analyse,
                    Order = 0,
                    Status = JobTaskStatus.Pending,
                },
                new()
                {
                    Id = "prepare.workitems",
                    Name = "WorkItems Prepare",
                    TaskKind = TaskKind.Prepare,
                    Phase = "Prepare",
                    Order = 1,
                    Status = JobTaskStatus.Pending,
                },
            }.AsReadOnly(),
            Phases = new List<JobPhaseSummary>
            {
                new() { Name = "Analyse", Order = 0, TaskIds = new[] { "analyse.inventory.org.project" } },
                new() { Name = "Prepare", Order = 1, TaskIds = new[] { "prepare.workitems" } },
            }.AsReadOnly()
        };

        var output = (string)buildMigrationWorkspace!.Invoke(
            null,
            [summary, taskList, null, new ProgressEvent { Stage = "Analyse.Starting" }])!;

        StringAssert.Contains(output, "Stages     [Analyse]  Prepare",
            "The stage strip should prefer explicit phase summaries over inferred task-kind group names.");
        StringAssert.Contains(output, "> Analyse",
            "The active phase heading should use the explicit Analyse phase summary.");
        StringAssert.Contains(output, "  Prepare",
            "The secondary phase heading should use the explicit Prepare phase summary.");
    }
}