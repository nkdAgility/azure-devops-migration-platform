// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Reflection;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

[TestClass]
public sealed class QueueDiscoveryDisplayTests
{
    [TestMethod]
    public void BuildDiscoveryDisplay_WhenDependencyAnalysisStarts_SwitchesToDependencyTable()
    {
        var queueType = typeof(QueueCommand);
        var stateType = queueType.GetNestedType("DiscoveryProgressState", BindingFlags.NonPublic)!;
        var taskListReceivedType = queueType.GetNestedType("TaskListReceived", BindingFlags.NonPublic)!;
        var stageAdvancedType = queueType.GetNestedType("StageAdvanced", BindingFlags.NonPublic)!;

        var initialFactory = stateType.GetMethod("Initial", BindingFlags.Public | BindingFlags.Static)!;
        var applyDiscovery = queueType.GetMethod("ApplyDiscovery", BindingFlags.NonPublic | BindingFlags.Static)!;
        var buildDiscoveryDisplay = queueType.GetMethod("BuildDiscoveryDisplay", BindingFlags.NonPublic | BindingFlags.Static)!;

        var state = initialFactory.Invoke(null, null)!;

        var taskList = new JobTaskList
        {
            Tasks = new List<JobTask>
            {
                new()
                {
                    Id = "capture.workitems.testorg.testproject",
                    Name = "WorkItems Capture",
                    TaskKind = TaskKind.Capture,
                    OrganisationUrl = "https://dev.azure.com/testorg",
                    ProjectName = "testproject",
                    Order = 0,
                    Status = JobTaskStatus.Completed,
                },
                new()
                {
                    Id = "analyse.dependencies",
                    Name = "Dependencies Analyse",
                    TaskKind = TaskKind.Analyse,
                    Order = 1,
                    Status = JobTaskStatus.Running,
                }
            }.AsReadOnly()
        };

        var taskListReceived = Activator.CreateInstance(taskListReceivedType, taskList, 0L)!;
        state = applyDiscovery.Invoke(null, [state, taskListReceived])!;

        var analysisEvent = new ProgressEvent
        {
            Module = "Dependencies",
            Stage = "Analysis",
            Message = "https://dev.azure.com/testorg|testproject",
            Metrics = new JobMetrics
            {
                Scope = new JobScopeCounters { WorkItemsTotal = 12 },
                Discovery = new DiscoveryCounters
                {
                    Dependencies = new DependencyCounters
                    {
                        WorkItemsAnalysed = 3,
                        ExternalLinksFound = 7,
                        CrossProjectLinks = 2,
                        CrossOrgLinks = 1
                    }
                }
            }
        };

        var stageAdvanced = Activator.CreateInstance(stageAdvancedType, analysisEvent)!;
        state = applyDiscovery.Invoke(null, [state, stageAdvanced])!;

        var renderable = (IRenderable)buildDiscoveryDisplay.Invoke(null, [state])!;

        var console = new TestConsole();
        console.Profile.Width = 160;
        console.Write(renderable);
        var output = console.Output;

        StringAssert.Contains(output, "Links", "Dependency analysis should render the dependency table headers.");
        StringAssert.Contains(output, "Cross Project", "Dependency analysis should render dependency-specific columns.");
        Assert.IsFalse(output.Contains("Revisions", StringComparison.Ordinal), "Dependency analysis should not keep rendering the inventory matrix once analysis has started.");
    }
}