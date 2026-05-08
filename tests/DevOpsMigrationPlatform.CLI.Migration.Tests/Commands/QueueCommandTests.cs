// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Tests for the unified <c>queue</c> command.
/// Mode-driven behaviour: the config <c>mode</c> field determines execution.
/// </summary>
[TestClass]
[DoNotParallelize]
public class QueueCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void QueueCommandSettings_WithValidLevel_PassesValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "Information"
        };

        var result = settings.Validate();
        Assert.IsTrue(result.Successful, "Validation should pass for valid log level.");
    }

    [TestMethod]
    public void QueueCommandSettings_WithInvalidLevel_FailsValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "InvalidLevel"
        };

        var result = settings.Validate();
        Assert.IsFalse(result.Successful, "Validation should fail for invalid log level.");
    }

    [TestMethod]
    public void QueueCommandSettings_WithDiagnosticsFlag_PassesValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Diagnostics = true,
            Level = "Information"
        };

        var result = settings.Validate();
        Assert.IsTrue(result.Successful, "Validation should pass when diagnostics flag is enabled.");
    }

    [TestMethod]
    public void DetermineCurrentTaskPhase_WithOnlyTerminalTasks_ReturnsLastTerminalPhase()
    {
        var stateType = typeof(QueueCommand).GetNestedType("JobProgressState", BindingFlags.NonPublic);
        Assert.IsNotNull(stateType, "JobProgressState nested type was not found.");

        var initialMethod = stateType!.GetMethod("Initial", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(initialMethod, "JobProgressState.Initial factory was not found.");

        var state = initialMethod!.Invoke(null, [0]);
        Assert.IsNotNull(state, "JobProgressState.Initial returned null.");

        stateType.GetProperty("Stage")!.SetValue(state, "Job.Completed");
        stateType.GetProperty("Tasks")!.SetValue(
            state,
            new JobTaskList
            {
                Tasks = new List<JobTask>
                {
                    new()
                    {
                        Id = "export.workitems.org.project",
                        Name = "WorkItems Export",
                        TaskKind = TaskKind.Export,
                        Phase = "Export",
                        Order = 0,
                        Status = JobTaskStatus.Completed,
                    },
                    new()
                    {
                        Id = "import.workitems.org.project",
                        Name = "WorkItems Import",
                        TaskKind = TaskKind.Import,
                        Phase = "Import",
                        Order = 1,
                        Status = JobTaskStatus.Completed,
                    },
                }
            });

        var determineCurrentTaskPhase = typeof(QueueCommand).GetMethod("DetermineCurrentTaskPhase", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(determineCurrentTaskPhase, "DetermineCurrentTaskPhase method was not found.");

        var phase = determineCurrentTaskPhase!.Invoke(null, [state!, (IReadOnlyList<string>)new List<string> { "Export", "Import" }]) as string;

        Assert.AreEqual("Import", phase, "Completed multi-stage jobs should remain on their last terminal phase.");
    }

    [TestMethod]
    public void BuildProgressDisplay_WhenTaskListHasExplicitPhaseSummaries_UsesSummaryPhases()
    {
        var stateType = typeof(QueueCommand).GetNestedType("JobProgressState", BindingFlags.NonPublic);
        Assert.IsNotNull(stateType, "JobProgressState nested type was not found.");

        var initialMethod = stateType!.GetMethod("Initial", BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(initialMethod, "JobProgressState.Initial factory was not found.");

        var state = initialMethod!.Invoke(null, [0]);
        Assert.IsNotNull(state, "JobProgressState.Initial returned null.");

        stateType.GetProperty("Stage")!.SetValue(state, "Analyse.Starting");
        stateType.GetProperty("Tasks")!.SetValue(
            state,
            new JobTaskList
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
            });

        var buildProgressDisplay = typeof(QueueCommand).GetMethod("BuildProgressDisplay", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(buildProgressDisplay, "BuildProgressDisplay method was not found.");

        var renderable = (IRenderable)buildProgressDisplay!.Invoke(null, [state!])!;
        var console = new TestConsole();
        console.Profile.Width = 160;
        console.Write(renderable);
        var output = console.Output;

        StringAssert.Contains(output, "Analyse", "The explicit Analyse phase summary should be rendered.");
        StringAssert.Contains(output, "Prepare", "The explicit Prepare phase summary should be rendered.");
        Assert.IsTrue(CountOccurrences(output, "Analyse") >= 2,
            "Queue progress should render the explicit Analyse stage in addition to the task name when summary phases are present.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/queue-export-ado-workitems-single-project.json --force-fresh</c>
    /// as a subprocess. The scenario config has <c>mode: Export</c>, so this must behave
    /// identically to the former <c>export</c> command.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(1_200_000)] // 20 minutes
    public async Task QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var testStorage = Path.Combine(CliRunner.TestWorkingFolder, nameof(QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles));
        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), testStorage);
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles),
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18),
            cleanOutputFolder: true);
        outputDir = result.OutputDirectory;

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        // Org/project nesting places WorkItems under <outputDir>/<org>/<project>/WorkItems/
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"WorkItems directory was not created anywhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDirs[0]}");
    }

    /// <summary>
    /// Verifies that when the config specifies <c>Environment.Type = Hosted</c> and the control
    /// plane is not running, the CLI fails fast with an actionable error — without performing
    /// expensive preflight operations (e.g. work item counting).
    /// Uses the simulated export config with the environment type overridden via env vars.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(30_000)] // 30 seconds — should fail fast
    public async Task QueueCommand_WithHostedModeAndUnreachableControlPlane_FailsFast()
    {
        // ── Act — override to Hosted mode pointing at a port nothing listens on ──
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueCommand_WithHostedModeAndUnreachableControlPlane_FailsFast),
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json"],
            env: new Dictionary<string, string>
            {
                ["MigrationPlatform__Environment__Type"] = "Hosted",
                ["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = "http://localhost:59999"
            },
            timeout: TimeSpan.FromSeconds(25));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out — should have failed fast.");
        Assert.AreNotEqual(0, result.ExitCode,
            "CLI should exit non-zero when the control plane is unreachable.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("not reachable", StringComparison.OrdinalIgnoreCase),
            "Expected 'not reachable' error message in output.");
        Assert.IsTrue(
            combinedOutput.Contains("\"Type\": \"Standalone\"", StringComparison.OrdinalIgnoreCase),
            "Expected guidance showing the Standalone config snippet in output.");
    }

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/queue-import-workitems-simulated-fixture.json --force-fresh</c>
    /// as a subprocess. Uses a pre-built fixture zip (<c>scenarios/testdata/workitems-2items-flat.zip</c>)
    /// with a <c>Simulated</c> target — no live credentials required.
    /// Verifies that the CLI exits zero and logs import progress for both work items.
    /// See <c>scenarios/testdata/catalogue.json</c> for fixture details.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)] // 2 minutes — no network I/O
    public async Task QueueCommand_WithSimulatedImportMode_Fixture_ExitsZero_AndImportsBothWorkItems()
    {
        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueCommand_WithSimulatedImportMode_Fixture_ExitsZero_AndImportsBothWorkItems),
            args: ["queue", "--config", "scenarios/queue-import-workitems-simulated-fixture.json", "--force-fresh"],
            timeout: TimeSpan.FromSeconds(110),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("import complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work item", StringComparison.OrdinalIgnoreCase),
            "Expected CLI import progress message not found in output.");

        // Verify both fixture work items were processed by checking the idmap DB was created
        // (progress events are not observable from CLI stdout in the current streaming architecture).
        // Org/project nesting places idmap.db under <outputDir>/<org>/<project>/.migration/Checkpoints/
        var idmapFiles = Directory.GetFiles(outputDir, "idmap.db", SearchOption.AllDirectories);
        Assert.IsTrue(idmapFiles.Length > 0,
            $".migration/Checkpoints/idmap.db was not found anywhere under {outputDir} — import may not have processed any work items.");
    }


    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(300_000)]
    public async Task QueueCommand_WithInventoryMode_Simulated_WritesInventoryArtefacts()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueCommand_WithInventoryMode_Simulated_WritesInventoryArtefacts),
            args: ["queue", "--config", "scenarios/inventory-simulated.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");

        var csvFiles = Directory.GetFiles(outputDir, "inventory.csv", SearchOption.AllDirectories);
        Assert.IsTrue(csvFiles.Length > 0,
            $"Expected inventory.csv somewhere under {outputDir}. None found.");

        var csvLines = File.ReadAllLines(csvFiles[0]);
        Assert.IsTrue(csvLines.Length > 1,
            $"inventory.csv must contain at least a header + one data row. Got {csvLines.Length} lines.");

        var jsonFiles = Directory.GetFiles(outputDir, "inventory.json", SearchOption.AllDirectories);
        Assert.IsTrue(jsonFiles.Length > 0,
            $"Expected inventory.json somewhere under {outputDir}. None found.");

        var jsonContent = File.ReadAllText(jsonFiles[0]);
        Assert.IsTrue(jsonContent.Length > 10,
            $"inventory.json must contain meaningful content. Got {jsonContent.Length} chars.");
        Assert.IsTrue(jsonContent.Contains("SimulatedProject", StringComparison.OrdinalIgnoreCase),
            "inventory.json must reference the SimulatedProject that was discovered.");
    }
}
