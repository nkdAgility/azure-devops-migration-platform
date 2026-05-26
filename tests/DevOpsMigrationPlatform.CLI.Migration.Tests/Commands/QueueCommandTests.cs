// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.ProjectLifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.Process.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.Operations;
using Microsoft.VisualStudio.Services.WebApi;
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
    /// Runs <c>devopsmigration queue --config scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json --force-fresh</c>
    /// as a subprocess. The scenario config has <c>mode: Export</c>, so this must behave
    /// identically to the former <c>export</c> command.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    public async Task Queue_Export_Sim_WritesRevisionFiles()
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

        var sourceProjectName = TryGetScenarioSourceProjectName("scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json");
        Assert.IsFalse(string.IsNullOrWhiteSpace(sourceProjectName),
            "Live export scenario must define MigrationPlatform.Source.Project.");
        var expectedWorkItemCount = await CountWorkItemsInProjectAsync(orgEnv, patEnv, sourceProjectName!);

        var testStorage = Path.Combine(CliRunner.TestWorkingFolder, nameof(Queue_Export_Sim_WritesRevisionFiles));
        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), testStorage);
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_Sim_WritesRevisionFiles),
            args: ["queue", "--config", "scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json", "--force-fresh"],
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

        var exportedWorkItemIds = revisionFiles
            .Select(Path.GetDirectoryName)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFileName(path!))
            .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
            .Select(folderName => folderName!.Split('-'))
            .Where(parts => parts.Length >= 3 && int.TryParse(parts[1], out _))
            .Select(parts => int.Parse(parts[1]))
            .Distinct()
            .Count();

        Assert.AreEqual(expectedWorkItemCount, exportedWorkItemIds,
            $"Expected exported work item count to match source project '{sourceProjectName}' count ({expectedWorkItemCount}), " +
            $"but found {exportedWorkItemIds} distinct work item IDs in exported revisions.");
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
    public async Task Queue_FailsFast_UnreachableControlPlane()
    {
        // ── Act — override to Hosted mode pointing at a port nothing listens on ──
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_FailsFast_UnreachableControlPlane),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json"],
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
    /// Runs <c>devopsmigration queue --config scenarios/SystemTest-Simulated-Import-WorkItems-Fixture.json --force-fresh</c>
    /// as a subprocess. Uses a pre-built fixture zip (<c>scenarios/testdata/workitems-2items-flat.zip</c>)
    /// with a <c>Simulated</c> target — no live credentials required.
    /// Verifies that the CLI exits zero and logs import progress for both work items.
    /// See <c>scenarios/testdata/catalogue.json</c> for fixture details.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    public async Task Queue_Import_Sim_Fixture_ImportsBothWorkItems()
    {
        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Import_Sim_Fixture_ImportsBothWorkItems),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Import-WorkItems-Fixture.json", "--force-fresh"],
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
    public async Task Queue_Inventory_Sim_WritesInventoryArtefacts()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Inventory_Sim_WritesInventoryArtefacts),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Inventory-WorkItems.json", "--force-fresh"],
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

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/SystemTest-Live-Export-TFS-WorkItems-SingleProject.json --force-fresh</c>
    /// as a subprocess against a live TeamFoundationServer instance.
    /// Verifies the CLI exits zero and writes revision files into the package.
    /// Requires <c>AZDEVOPS_SYSTEM_TEST_ORG</c> and <c>AZDEVOPS_SYSTEM_TEST_PAT</c> to be set.
    /// </summary>
    /// <remarks>
    /// NOTE: The ADO and TFS credentials used for system testing are IDENTICAL.
    /// <c>AZDEVOPS_SYSTEM_TEST_ORG</c> holds the TFS collection URL and <c>AZDEVOPS_SYSTEM_TEST_PAT</c>
    /// holds the PAT for both ADO and TFS test targets. Do not introduce separate TFS_* env vars.
    /// </remarks>
    [TestMethod]
    [Timeout(180000)]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestCategory("SystemTest_Live_TFS")]
    public async Task Queue_Export_TFS_WritesRevisionFiles()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        // NOTE: ADO and TFS test credentials are identical — use AZDEVOPS_SYSTEM_TEST_* for both.
        var tfsUrlEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var tfsPatEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(tfsUrlEnv) || string.IsNullOrEmpty(tfsPatEnv))
        {
            Assert.Fail(
                "System test failed: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "These credentials are shared between ADO and TFS test targets. " +
                "See docs/live-system-testing-guide.md for setup instructions.");
            return;
        }

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_TFS_WritesRevisionFiles),
            args: ["queue", "--config", "scenarios/SystemTest-Live-Export-TFS-WorkItems-SingleProject.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18),
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
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"WorkItems directory was not created anywhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDirs[0]}");
    }

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture.json --force-fresh</c>
    /// as a subprocess against a live AzureDevOpsServices target, using the pre-built fixture zip.
    /// Verifies the CLI exits zero and the idmap checkpoint database is created.
    /// Requires <c>AZDEVOPS_SYSTEM_TEST_ORG</c>, <c>AZDEVOPS_SYSTEM_TEST_PAT</c>, and
    /// <c>AZDEVOPS_SYSTEM_TEST_PROJECT</c> to be set.
    /// See <c>scenarios/testdata/catalogue.json</c> for fixture details.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    public async Task Queue_Import_ADO_Fixture_CreatesIdmap()
    {
        const int expectedImportedWorkItemCount = 2;
        const string sourceFixtureType = "User Story";
        const string sourceFixtureState = "Active";

        // ── Guard ─────────────────────────────────────────────────────────
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Fail(
                "System test failed: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/live-system-testing-guide.md for setup instructions.");
            return;
        }

        ProjectLifecycleRecord? lifecycleRecord = null;
        var lifecycleService = CreateAzureDevOpsProjectLifecycleService();
        string? runtimeConfigPath = null;
        try
        {
            const string preferredProcessName = "Agile";

            lifecycleRecord = await CreateTemporaryAzureDevOpsProjectAsync(
                lifecycleService,
                orgEnv,
                patEnv,
                preferredProcessName,
                CancellationToken.None);

            var availableTypes = await GetWorkItemTypeNamesAsync(
                orgEnv,
                patEnv,
                lifecycleRecord.ProjectName,
                CancellationToken.None);
            var mappedTargetType = ResolveTargetWorkItemTypeForFixture(sourceFixtureType, availableTypes);
            var availableStates = await GetWorkItemStateNamesAsync(
                orgEnv,
                patEnv,
                lifecycleRecord.ProjectName,
                mappedTargetType,
                CancellationToken.None);
            var mappedTargetState = ResolveTargetStateForFixture(sourceFixtureState, availableStates);
            runtimeConfigPath = CreateLiveImportConfigForProject(
                lifecycleRecord.ProjectName,
                sourceFixtureType,
                mappedTargetType,
                sourceFixtureState,
                mappedTargetState);
            var runtimeConfig = File.ReadAllText(runtimeConfigPath);
            Assert.IsTrue(runtimeConfig.Contains("\"Strategy\": \"TargetHyperlink\"", StringComparison.Ordinal),
                "Runtime import config must override WorkItemResolutionStrategy.Strategy to 'TargetHyperlink'.");
            var baselineWorkItemCount = await CountWorkItemsInProjectAsync(orgEnv, patEnv, lifecycleRecord.ProjectName);

            // ── Act ───────────────────────────────────────────────────────────
            var result = await CliRunner.RunTestAsync(
                testName: nameof(Queue_Import_ADO_Fixture_CreatesIdmap),
                args: ["queue", "--config", runtimeConfigPath, "--force-fresh"],
                timeout: TimeSpan.FromSeconds(90),
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

            var idmapFiles = Directory.GetFiles(outputDir, "idmap.db", SearchOption.AllDirectories);
            Assert.IsTrue(idmapFiles.Length > 0,
                $"idmap.db was not found anywhere under {outputDir} — import may not have processed any work items.");

            var importedCount = await CountWorkItemsInProjectAsync(orgEnv, patEnv, lifecycleRecord.ProjectName);
            var createdCount = importedCount - baselineWorkItemCount;
            Assert.AreEqual(expectedImportedWorkItemCount, createdCount,
                $"Expected {expectedImportedWorkItemCount} imported work items to be created in temporary project '{lifecycleRecord.ProjectName}', " +
                $"but baseline={baselineWorkItemCount}, final={importedCount}, created={createdCount}.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(runtimeConfigPath) && File.Exists(runtimeConfigPath))
                File.Delete(runtimeConfigPath);

            if (lifecycleRecord is not null)
                await lifecycleService.TeardownAsync(lifecycleRecord, CancellationToken.None);
        }
    }

    private static async Task<ProjectLifecycleRecord> CreateTemporaryAzureDevOpsProjectAsync(
        IProjectLifecycleService lifecycleService,
        string organisationUrl,
        string accessToken,
        string? preferredProcessName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredProcessName))
        {
            var requestedContext = CreateAzureDevOpsLifecycleContext(organisationUrl, accessToken, preferredProcessName);
            var requestedRecord = await lifecycleService.CreateAsync(requestedContext, cancellationToken);
            if (requestedRecord.CreateResult == ProjectLifecycleCreateResult.Succeeded)
                return requestedRecord;

            Assert.Fail($"Lifecycle setup failed for explicitly requested process '{preferredProcessName}': {requestedRecord.CreateFailureReason}");
        }

        var processCandidates = new List<string>();
        processCandidates.Add("Agile");
        processCandidates.Add("Scrum");
        processCandidates.Add("CMMI");
        processCandidates.AddRange(
            await GetAvailableProcessNamesAsync(organisationUrl, accessToken, cancellationToken));

        ProjectLifecycleRecord? lastFailure = null;
        foreach (var processName in processCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var context = CreateAzureDevOpsLifecycleContext(organisationUrl, accessToken, processName);
            var record = await lifecycleService.CreateAsync(context, cancellationToken);
            if (record.CreateResult == ProjectLifecycleCreateResult.Succeeded)
                return record;

            lastFailure = record;
            if (record.CreateFailureReason?.IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            Assert.Fail($"Lifecycle setup failed for process '{processName}': {record.CreateFailureReason}");
        }

        Assert.Fail($"Lifecycle setup failed for all candidate processes. Last failure: {lastFailure?.CreateFailureReason}");
        return null!;
    }


    private static async Task<IReadOnlyList<string>> GetAvailableProcessNamesAsync(
        string organisationUrl,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = new OrganisationEndpoint
        {
            Type = "AzureDevOpsServices",
            ResolvedUrl = organisationUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = accessToken
            }
        };

        var clientFactory = new TestAzureDevOpsClientFactory();
        var processClient = await clientFactory.CreateProcessClientAsync(endpoint, cancellationToken);
        var processes = await processClient.GetListOfProcessesAsync(cancellationToken: cancellationToken);
        return processes
            .Select(p => p.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<HashSet<string>> GetWorkItemTypeNamesAsync(
        string organisationUrl,
        string accessToken,
        string projectName,
        CancellationToken cancellationToken)
    {
        var endpoint = new OrganisationEndpoint
        {
            Type = "AzureDevOpsServices",
            ResolvedUrl = organisationUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = accessToken
            }
        };

        var clientFactory = new TestAzureDevOpsClientFactory();
        var workItemClient = await clientFactory.CreateWorkItemClientAsync(endpoint, cancellationToken);
        var workItemTypes = await workItemClient.GetWorkItemTypesAsync(projectName, cancellationToken: cancellationToken);

        return workItemTypes
            .Select(type => type.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<string?> TryResolveProcessNameForExistingProjectAsync(
        string organisationUrl,
        string accessToken,
        string? existingProjectName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(existingProjectName))
            return null;

        var endpoint = new OrganisationEndpoint
        {
            Type = "AzureDevOpsServices",
            ResolvedUrl = organisationUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = accessToken
            }
        };

        var clientFactory = new TestAzureDevOpsClientFactory();
        var projectClient = await clientFactory.CreateProjectClientAsync(endpoint, cancellationToken);
        var processClient = await clientFactory.CreateProcessClientAsync(endpoint, cancellationToken);

        var existingProject = await projectClient.GetProject(
            existingProjectName,
            includeCapabilities: true,
            includeHistory: false,
            userState: null);
        if (existingProject.Capabilities is null)
            return null;

        if (!existingProject.Capabilities.TryGetValue(
                TeamProjectCapabilitiesConstants.ProcessTemplateCapabilityName,
                out var processCapability))
            return null;

        if (!processCapability.TryGetValue(
                TeamProjectCapabilitiesConstants.ProcessTemplateCapabilityTemplateTypeIdAttributeName,
                out var processTypeId) ||
            !Guid.TryParse(processTypeId, out var processTypeGuid))
            return null;

        var processes = await processClient.GetListOfProcessesAsync(cancellationToken: cancellationToken);
        var process = processes.FirstOrDefault(p => p.TypeId == processTypeGuid);
        return process?.Name;
    }

    private static ProjectLifecycleContext CreateAzureDevOpsLifecycleContext(
        string organisationUrl,
        string accessToken,
        string processName)
    {
        return new ProjectLifecycleContext
        {
            RunId = Guid.NewGuid().ToString("N"),
            ConnectorType = "AzureDevOpsServices",
            NamePrefix = "importsys",
            ProcessName = processName,
            Endpoint = new OrganisationEndpoint
            {
                Type = "AzureDevOpsServices",
                ResolvedUrl = organisationUrl,
                Authentication = new OrganisationEndpointAuthentication
                {
                    Type = AuthenticationType.AccessToken,
                    ResolvedAccessToken = accessToken
                }
            }
        };
    }

    private static IProjectLifecycleService CreateAzureDevOpsProjectLifecycleService()
    {
        var clientFactory = new TestAzureDevOpsClientFactory();
        var processProvider = new AzureDevOpsProjectProcessProvider(clientFactory);
        var processService = new SingleConnectorProjectProcessService(processProvider);
        var lifecycleProvider = new AzureDevOpsProjectLifecycleProvider(clientFactory, processService);

        return new ProjectLifecycleService(
            new ProjectLifecycleNameGenerator(),
            lifecycleProvider,
            NullLogger<ProjectLifecycleService>.Instance);
    }

    private static async Task<int> CountWorkItemsInProjectAsync(string organisationUrl, string accessToken, string projectName)
    {
        var endpoint = new OrganisationEndpoint
        {
            Type = "AzureDevOpsServices",
            ResolvedUrl = organisationUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = accessToken
            }
        };

        var clientFactory = new TestAzureDevOpsClientFactory();
        var workItemClient = await clientFactory.CreateWorkItemClientAsync(endpoint, CancellationToken.None);
        var queryResult = await workItemClient.QueryByWiqlAsync(
            new Wiql { Query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project" },
            project: projectName,
            cancellationToken: CancellationToken.None);

        return queryResult.WorkItems is null ? 0 : queryResult.WorkItems.Count();
    }

    private static string ResolveTargetWorkItemTypeForFixture(
        string sourceFixtureType,
        IReadOnlyCollection<string> availableTypes)
    {
        if (availableTypes.Contains(sourceFixtureType, StringComparer.OrdinalIgnoreCase))
            return sourceFixtureType;

        if (availableTypes.Contains("Product Backlog Item", StringComparer.OrdinalIgnoreCase))
            return "Product Backlog Item";

        if (availableTypes.Contains("Issue", StringComparer.OrdinalIgnoreCase))
            return "Issue";

        Assert.Fail(
            $"Temporary target project does not contain '{sourceFixtureType}', 'Product Backlog Item', or 'Issue'. " +
            $"Available types: {string.Join(", ", availableTypes.OrderBy(type => type, StringComparer.OrdinalIgnoreCase))}.");
        return sourceFixtureType;
    }

    private static string ResolveTargetStateForFixture(
        string sourceFixtureState,
        IReadOnlyCollection<string> availableStates)
    {
        if (availableStates.Contains(sourceFixtureState, StringComparer.OrdinalIgnoreCase))
            return sourceFixtureState;

        if (availableStates.Contains("New", StringComparer.OrdinalIgnoreCase))
            return "New";

        var fallbackState = availableStates.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fallbackState))
            return fallbackState;

        Assert.Fail("Temporary target project work item type has no available states to map fixture value.");
        return sourceFixtureState;
    }

    private static string CreateLiveImportConfigForProject(
        string projectName,
        string sourceType,
        string targetType,
        string sourceState,
        string targetState)
    {
        var templatePath = ResolveScenarioTemplatePath("scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture.json");
        var tempConfigPath = Path.Combine(
            Path.GetTempPath(),
            $"SystemTest-Live-Import-AzureDevOps-{Guid.NewGuid():N}.json");

        var root = JsonNode.Parse(File.ReadAllText(templatePath))
            ?? throw new InvalidOperationException($"Could not parse scenario template '{templatePath}'.");
        var migrationPlatform = root["MigrationPlatform"]?.AsObject()
            ?? throw new InvalidOperationException("Scenario template does not contain MigrationPlatform object.");
        var target = migrationPlatform["Target"]?.AsObject()
            ?? throw new InvalidOperationException("Scenario template does not contain Target object.");

        target["Project"] = projectName;

        var extensions = migrationPlatform["Modules"]?["WorkItems"]?["Extensions"]?.AsObject()
            ?? throw new InvalidOperationException("Scenario template does not contain Modules.WorkItems.Extensions object.");
        extensions["WorkItemResolutionStrategy"] = new JsonObject
        {
            ["Strategy"] = "TargetHyperlink",
            ["UrlPattern"] = string.Empty
        };
        migrationPlatform["Tools"] = new JsonObject
        {
            ["FieldTransform"] = new JsonObject
            {
                ["Enabled"] = true,
                ["TransformGroups"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Name"] = "MapFixtureWorkItemType",
                        ["Enabled"] = true,
                        ["Transforms"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["Type"] = "MapValue",
                                ["Enabled"] = true,
                                ["Field"] = "System.WorkItemType",
                                ["ValueMap"] = new JsonObject
                                {
                                    [sourceType] = targetType
                                }
                            },
                            new JsonObject
                            {
                                ["Type"] = "MapValue",
                                ["Enabled"] = true,
                                ["Field"] = "System.TeamProject",
                                ["ValueMap"] = new JsonObject
                                {
                                    ["FixtureProject"] = projectName
                                }
                            },
                            new JsonObject
                            {
                                ["Type"] = "MapValue",
                                ["Enabled"] = true,
                                ["Field"] = "System.State",
                                ["ValueMap"] = new JsonObject
                                {
                                    [sourceState] = targetState
                                }
                            }
                        }
                    }
                }
            }
        };

        File.WriteAllText(
            tempConfigPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return tempConfigPath;
    }

    private static async Task<IReadOnlyCollection<string>> GetWorkItemStateNamesAsync(
        string organisationUrl,
        string accessToken,
        string projectName,
        string workItemType,
        CancellationToken cancellationToken)
    {
        var endpoint = new OrganisationEndpoint
        {
            Type = "AzureDevOpsServices",
            ResolvedUrl = organisationUrl,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType.AccessToken,
                ResolvedAccessToken = accessToken
            }
        };

        var clientFactory = new TestAzureDevOpsClientFactory();
        var workItemClient = await clientFactory.CreateWorkItemClientAsync(endpoint, cancellationToken);
        var typeDefinition = await workItemClient.GetWorkItemTypeAsync(projectName, workItemType, cancellationToken: cancellationToken);
        return typeDefinition.States
            .Select(state => state.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveScenarioTemplatePath(string relativePath)
    {
        var repoRoot = CliRunner.FindRepoRoot();
        return Path.Combine(repoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string? TryGetScenarioTargetProjectName(string relativeTemplatePath)
    {
        var templatePath = ResolveScenarioTemplatePath(relativeTemplatePath);
        if (!File.Exists(templatePath))
            return null;

        var root = JsonNode.Parse(File.ReadAllText(templatePath));
        return root?["MigrationPlatform"]?["Target"]?["Project"]?.GetValue<string>();
    }

    private static string? TryGetScenarioSourceProjectName(string relativeTemplatePath)
    {
        var templatePath = ResolveScenarioTemplatePath(relativeTemplatePath);
        if (!File.Exists(templatePath))
            return null;

        var root = JsonNode.Parse(File.ReadAllText(templatePath));
        return root?["MigrationPlatform"]?["Source"]?["Project"]?.GetValue<string>();
    }

    private static string CreateLiveImportFieldMissingConfigForProject(string projectName)
    {
        var templatePath = ResolveScenarioTemplatePath("scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture-FieldMissing.json");
        var tempConfigPath = Path.Combine(
            Path.GetTempPath(),
            $"SystemTest-Live-Import-AzureDevOps-FieldMissing-{Guid.NewGuid():N}.json");

        var root = JsonNode.Parse(File.ReadAllText(templatePath))
            ?? throw new InvalidOperationException($"Could not parse scenario template '{templatePath}'.");
        var migrationPlatform = root["MigrationPlatform"]?.AsObject()
            ?? throw new InvalidOperationException("Scenario template does not contain MigrationPlatform object.");
        var target = migrationPlatform["Target"]?.AsObject()
            ?? throw new InvalidOperationException("Scenario template does not contain Target object.");

        target["Project"] = projectName;

        File.WriteAllText(
            tempConfigPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return tempConfigPath;
    }

    private sealed class SingleConnectorProjectProcessService : IProjectProcessService
    {
        private readonly IProjectProcessProvider _provider;

        public SingleConnectorProjectProcessService(IProjectProcessProvider provider)
            => _provider = provider ?? throw new ArgumentNullException(nameof(provider));

        public Task<string> ResolveProcessTypeIdAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
            => _provider.ResolveProcessTypeIdAsync(context, cancellationToken);
    }

    private sealed class TestAzureDevOpsClientFactory : IAzureDevOpsClientFactory
    {
        public Task<ProjectHttpClient> CreateProjectClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<ProjectHttpClient>(cancellationToken);

        public Task<WorkItemTrackingHttpClient> CreateWorkItemClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

        public Task<GitHttpClient> CreateGitClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<GitHttpClient>(cancellationToken);

        public Task<TeamHttpClient> CreateTeamClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<TeamHttpClient>(cancellationToken);

        public Task<WorkHttpClient> CreateWorkClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<WorkHttpClient>(cancellationToken);

        public Task<OperationsHttpClient> CreateOperationsClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<OperationsHttpClient>(cancellationToken);

        public Task<WorkItemTrackingProcessHttpClient> CreateProcessClientAsync(OrganisationEndpoint endpoint, CancellationToken cancellationToken = default)
            => CreateConnection(endpoint).GetClientAsync<WorkItemTrackingProcessHttpClient>(cancellationToken);

        private static VssConnection CreateConnection(OrganisationEndpoint endpoint)
        {
            var pat = endpoint.Authentication.ResolvedAccessToken;
            VssCredentials credentials = string.IsNullOrWhiteSpace(pat)
                ? new VssCredentials()
                : (VssCredentials)new VssBasicCredential(string.Empty, pat);
            return new VssConnection(new Uri(endpoint.ResolvedUrl), credentials);
        }
    }

    /// <summary>
    /// Verifies that when the provenance field configured in <c>WorkItemResolutionStrategy.FieldName</c>
    /// does not exist in the target project (TF51005), the CLI exits non-zero and writes
    /// <c>errors.json</c> to the package root with a <c>ValidationError</c> category entry.
    /// Uses the same ADO fixture config as <see cref="Queue_Import_ADO_Fixture_CreatesIdmap"/>.
    /// Requires <c>AZDEVOPS_SYSTEM_TEST_ORG</c> and <c>AZDEVOPS_SYSTEM_TEST_PAT</c> to be set,
    /// and the target project must NOT have the <c>Custom.ReflectedWorkItemId</c> field.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    public async Task Queue_Import_ADO_Fixture_FieldMissing_WritesErrorsJson()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/live-system-testing-guide.md for setup instructions.");
            return;
        }

        ProjectLifecycleRecord? lifecycleRecord = null;
        var lifecycleService = CreateAzureDevOpsProjectLifecycleService();
        string? runtimeConfigPath = null;
        try
        {
            var preferredProcessName = await TryResolveProcessNameForExistingProjectAsync(
                orgEnv,
                patEnv,
                Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PROJECT") ?? TryGetScenarioTargetProjectName("scenarios/SystemTest-Live-Import-AzureDevOps-WorkItems-Fixture-FieldMissing.json"),
                CancellationToken.None);

            lifecycleRecord = await CreateTemporaryAzureDevOpsProjectAsync(
                lifecycleService,
                orgEnv,
                patEnv,
                preferredProcessName,
                CancellationToken.None);

            runtimeConfigPath = CreateLiveImportFieldMissingConfigForProject(lifecycleRecord.ProjectName);
            var baselineWorkItemCount = await CountWorkItemsInProjectAsync(orgEnv, patEnv, lifecycleRecord.ProjectName);

            // ── Act ───────────────────────────────────────────────────────────
            var result = await CliRunner.RunTestAsync(
                testName: nameof(Queue_Import_ADO_Fixture_FieldMissing_WritesErrorsJson),
                args: ["queue", "--config", runtimeConfigPath, "--force-fresh"],
                timeout: TimeSpan.FromSeconds(55),
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
            Assert.AreNotEqual(0, result.ExitCode,
                "CLI should exit non-zero when the provenance field is missing (TF51005).");

            var errorsJsonFiles = Directory.GetFiles(outputDir, "errors.json", SearchOption.AllDirectories);
            Assert.IsTrue(errorsJsonFiles.Length > 0,
                $"errors.json was not found anywhere under '{outputDir}'. " +
                "JobPlanExecutor should write it on any blocking task failure.");
            var errorsJsonPath = errorsJsonFiles[0];

            var errorsJson = File.ReadAllText(errorsJsonPath);
            Console.WriteLine("=== errors.json ===");
            Console.WriteLine(errorsJson);

            StringAssert.Contains(errorsJson, "errors",
                "errors.json must contain an 'errors' array.");
            StringAssert.Contains(errorsJson, "MigrationException",
                "errors.json exceptionType should be MigrationException for TF51005.");
            StringAssert.Contains(errorsJson, "ReflectedWorkItemId",
                "errors.json message should reference the missing field name.");

            var finalWorkItemCount = await CountWorkItemsInProjectAsync(orgEnv, patEnv, lifecycleRecord.ProjectName);
            Assert.AreEqual(baselineWorkItemCount, finalWorkItemCount,
                $"Field-missing failure must not create work items in temporary project '{lifecycleRecord.ProjectName}'.");
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(runtimeConfigPath) && File.Exists(runtimeConfigPath))
                File.Delete(runtimeConfigPath);

            if (lifecycleRecord is not null)
                await lifecycleService.TeardownAsync(lifecycleRecord, CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/SystemTest-Live-Import-TFS-WorkItems-Fixture.json --force-fresh</c>
    /// as a subprocess against a live TeamFoundationServer target, using the pre-built fixture zip.
    /// Verifies the CLI exits zero and the idmap checkpoint database is created.
    /// Requires <c>AZDEVOPS_SYSTEM_TEST_ORG</c> and <c>AZDEVOPS_SYSTEM_TEST_PAT</c> to be set.
    /// See <c>scenarios/testdata/catalogue.json</c> for fixture details.
    /// </summary>
    /// <remarks>
    /// NOTE: The ADO and TFS credentials used for system testing are IDENTICAL.
    /// <c>AZDEVOPS_SYSTEM_TEST_ORG</c> holds the TFS collection URL and <c>AZDEVOPS_SYSTEM_TEST_PAT</c>
    /// holds the PAT for both ADO and TFS test targets. Do not introduce separate TFS_* env vars.
    /// </remarks>
    [TestMethod]
    [Timeout(120000)]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestCategory("SystemTest_Live_TFS")]
    public async Task Queue_Import_TFS_Fixture_CreatesIdmap()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        // NOTE: ADO and TFS test credentials are identical — use AZDEVOPS_SYSTEM_TEST_* for both.
        var tfsUrlEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var tfsPatEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(tfsUrlEnv) || string.IsNullOrEmpty(tfsPatEnv))
        {
            Assert.Fail(
                "System test failed: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "These credentials are shared between ADO and TFS test targets. " +
                "See docs/live-system-testing-guide.md for setup instructions.");
            return;
        }

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Import_TFS_Fixture_CreatesIdmap),
            args: ["queue", "--config", "scenarios/SystemTest-Live-Import-TFS-WorkItems-Fixture.json", "--force-fresh"],
            timeout: TimeSpan.FromSeconds(90),
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

        var idmapFiles = Directory.GetFiles(outputDir, "idmap.db", SearchOption.AllDirectories);
        Assert.IsTrue(idmapFiles.Length > 0,
            $"idmap.db was not found anywhere under {outputDir} — import may not have processed any work items.");
    }
}
