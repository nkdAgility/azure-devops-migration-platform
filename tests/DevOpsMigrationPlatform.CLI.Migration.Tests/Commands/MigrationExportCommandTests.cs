using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Utilities;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using DevOpsMigrationPlatform.Infrastructure.Services;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class MigrationExportCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void MigrationExportCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DevOpsMigrationPlatform.CLI.Migration.Commands.MigrationExportCommand();
        Assert.IsNotNull(command);
    }

    // ── System tests ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task MigrationExportCommand_SystemTest_MissingEnvironmentVars_MarksInconclusive()
    {
        var configuration = SystemTestConfiguration.LoadFromEnvironment();

        if (configuration.IsConfigured)
        {
            Assert.Inconclusive("Cannot test missing environment variables when they are actually present.");
            return;
        }

        var errorMessage = configuration.GetConfigurationErrorMessage();

        Assert.IsFalse(configuration.IsConfigured);
        Assert.IsTrue(errorMessage.Contains("Environment variables not configured"),
            "Error message should mention missing environment variables");
        Assert.IsTrue(errorMessage.Contains("AZDEVOPS_SYSTEM_TEST_ORG"),
            "Error message should mention the org variable");
        Assert.IsTrue(errorMessage.Contains("AZDEVOPS_SYSTEM_TEST_PAT"),
            "Error message should mention the token variable");

        Console.WriteLine($"Validated missing env-var handling: {errorMessage}");
        await Task.CompletedTask;
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task MigrationExportCommand_SystemTest_AdoSingleProject_ScenarioFile_ExportsPackage()
    {
        // Arrange – guard on required env vars
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Inconclusive(
                "System test skipped: AZDEVOPS_DEV_ORG and AZDEVOPS_DEV_PAT environment variables must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var scenarioFile = FindScenarioFile("export-ado-workitems-single-project.json");
        Assert.IsNotNull(scenarioFile,
            "Could not locate scenarios/export-ado-workitems-single-project.json relative to the test output directory");

        var configService = new ConfigurationService(NullLogger<ConfigurationService>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var config = await configService.LoadConfigurationAsync(scenarioFile, cts.Token);

        // Resolve $ENV: references in config
        var orgUrl  = TokenResolver.Resolve(config.Source?.Url)
                      ?? throw new InvalidOperationException("Source URL could not be resolved.");
        var project = config.Source?.Project
                      ?? throw new InvalidOperationException("Source project is required.");

        // Output paths
        var outputDir = config.Artefacts.ExpandedPath;
        var zipPath   = outputDir.TrimEnd(Path.DirectorySeparatorChar) + ".zip";

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        Directory.CreateDirectory(outputDir);

        // Build MigrationJob – mirrors what MigrationExportCommand does
        var job = new MigrationJob
        {
            JobId         = Guid.NewGuid().ToString(),
            ConfigVersion = config.ConfigVersion,
            Mode          = "Export",
            Source        = new MigrationJobEndpoint
            {
                Type           = config.Source!.Type,
                Url            = orgUrl,
                Project        = project,
                Authentication = config.Source.Authentication
            },
            Modules =
            [
                new MigrationJobModule
                {
                    Name   = "WorkItems",
                    Scopes =
                    [
                        new MigrationJobModuleScope
                        {
                            Type       = "wiql",
                            Parameters = new Dictionary<string, object?> { ["includeAttachments"] = false }
                        }
                    ]
                }
            ]
        };

        // Wire up infrastructure directly – no ControlPlane or DI container needed for a module-level system test
        var clientFactory = new AzureDevOpsClientFactory();
        var mapper        = new AzureDevOpsWorkItemRevisionMapper();
        var registry      = new AzureDevOpsAttachmentRegistry();
        var sourceFactory = new AzureDevOpsWorkItemRevisionSourceFactory(clientFactory, mapper, registry);
        var module        = new WorkItemsModule(sourceFactory, NullLogger<WorkItemsModule>.Instance);

        var artefactStore = new FileSystemArtefactStore(outputDir);
        var stateStore    = new FileSystemStateStore(outputDir);
        var progressSink  = new NullProgressSink();

        var context = new ExportContext
        {
            Job          = job,
            ArtefactStore = artefactStore,
            StateStore   = stateStore,
            ProgressSink = progressSink
        };

        // Act – run the real export
        await module.ExportAsync(context, cts.Token);

        // Assert – at least one revision.json written under WorkItems/
        var workItemsDir   = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"WorkItems directory was not created under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            "At least one revision.json should have been exported");
        Console.WriteLine($"Exported {revisionFiles.Length} revision(s) to {outputDir}");

        // Zip the package
        ZipFile.CreateFromDirectory(outputDir, zipPath);
        Assert.IsTrue(File.Exists(zipPath), $"Zip file was not created at {zipPath}");

        using var zip = ZipFile.OpenRead(zipPath);
        var workItemEntries = zip.Entries
            .Where(e => e.FullName.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.IsTrue(workItemEntries.Count > 0,
            "Zip file should contain WorkItems entries");

        Console.WriteLine($"Zip: {zipPath}");
        Console.WriteLine($"Zip WorkItems entries: {workItemEntries.Count}");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string? FindScenarioFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "scenarios", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    private sealed class NullProgressSink : IProgressSink
    {
        public void Emit(ProgressEvent evt) { }
    }
}
