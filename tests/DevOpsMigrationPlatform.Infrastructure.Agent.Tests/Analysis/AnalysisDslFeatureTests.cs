// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Analysis;

[TestClass]
public sealed class AnalysisDslFeatureTests
{
    [TestMethod]
    [DataRow("AzureDevOpsServices")]
    [DataRow("Simulated")]
    [DataRow("TeamFoundationServer")]
    public async Task Dependencies_AnalyserRuns_ProducesDependenciesCsv(string connectorType)
    {
        var result = await DependencyAnalysisScenario
            .For(connectorType)
            .WithCrossProjectLinks()
            .RunAsync();

        result.ShouldProduceDependenciesCsvWithAtLeastOneDataRow();
    }

    [TestMethod]
    [DataRow("AzureDevOpsServices")]
    [DataRow("Simulated")]
    [DataRow("TeamFoundationServer")]
    public async Task Dependencies_NoLinksFound_EmitsWarning(string connectorType)
    {
        var result = await DependencyAnalysisScenario
            .For(connectorType)
            .WithoutCrossProjectLinks()
            .RunAsync();

        result.ShouldEmitZeroOutputWarning();
    }

    [TestMethod]
    [DataRow("AzureDevOpsServices")]
    [DataRow("Simulated")]
    [DataRow("TeamFoundationServer")]
    public void Dependencies_RegisteredWithInventoryJob_RunsAfterInventoryPhase(string connectorType)
    {
        DependencyAnalysisScenario
            .For(connectorType)
            .ShouldRunInAnalysePhaseAfterInventory();
    }

    [TestMethod]
    public async Task Inventory_AllModulesComplete_ConsolidatedInventoryJsonWritten()
    {
        var result = await InventoryAnalysisScenario
            .WithAllInventoryCapableModulesEnabled()
            .RunAsync();

        result.ShouldWriteRootInventoryJson();
    }

    [TestMethod]
    public async Task Inventory_AllModulesComplete_InventoryCsvWritten()
    {
        var result = await InventoryAnalysisScenario
            .WithAllInventoryCapableModulesEnabled()
            .RunAsync();

        result.ShouldWriteInventoryCsvWithAtLeastOneDataRow();
    }

    [TestMethod]
    public async Task Inventory_ZeroCountModule_EmitsWarning()
    {
        var result = await InventoryAnalysisScenario
            .WithAllInventoryCapableModulesEnabled()
            .WithOneModuleInventoryMissing()
            .RunAsync();

        result.ShouldEmitMissingInventoryWarning();
    }

    private sealed class DependencyAnalysisScenario
    {
        private static readonly IReadOnlyList<string> s_dependencyRows =
        [
            "1,2,ProjA,https://org,4,5,ProjB,https://orgB,6",
            "7,8,ProjA,https://org,9,10,ProjB,https://orgB,11"
        ];

        private readonly string _connectorType;
        private IReadOnlyList<string> _rows = s_dependencyRows;

        private DependencyAnalysisScenario(string connectorType) => _connectorType = connectorType;

        public static DependencyAnalysisScenario For(string connectorType) => new(connectorType);

        public DependencyAnalysisScenario WithCrossProjectLinks()
        {
            _rows = s_dependencyRows;
            return this;
        }

        public DependencyAnalysisScenario WithoutCrossProjectLinks()
        {
            _rows = [];
            return this;
        }

        public async Task<DependencyAnalysisResult> RunAsync()
        {
            var package = PackageTestFactory.CreateLooseMock();
            var logger = new CapturingLogger<DependencyAnalyser>();
            var analyser = CreateDependencyAnalyser(_rows, logger);

            await analyser.AnalyseAsync(CreateDependencyContext(_connectorType, package.Object), CancellationToken.None);

            var csv = await ReadTextAsync(package.Object, "analysis/dependencies.csv");
            return new DependencyAnalysisResult(csv, logger.Entries);
        }

        public void ShouldRunInAnalysePhaseAfterInventory()
        {
            var analyser = CreateDependencyAnalyser(s_dependencyRows, new CapturingLogger<DependencyAnalyser>());
            Assert.IsTrue(
                analyser.DependsOn.Any(d => d.ModuleType == typeof(InventoryAnalyser) && d.Phase == DependencyPhase.Analyse),
                "Dependency analysis must be scheduled after inventory in analyse phase ordering.");
        }

        private static DependencyAnalyser CreateDependencyAnalyser(IReadOnlyList<string> csvRows, ILogger<DependencyAnalyser> logger)
        {
            var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
            factory.Setup(f => f.Create(It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(), It.IsAny<JobPolicies>()))
                .Returns(Mock.Of<IDependencyDiscoveryService>());

            var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
            orchestrator
                .Setup(o => o.AnalyseAsync(
                    It.IsAny<IDependencyDiscoveryService>(),
                    It.IsAny<OrganisationsAnalyseContext>(),
                    It.IsAny<JobPolicies>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .Returns<IDependencyDiscoveryService, OrganisationsAnalyseContext, JobPolicies, int, CancellationToken>(
                    async (_, context, _, _, ct) =>
                    {
                        var content = "SourceId,TargetId,SourceProject,SourceOrganisationUrl,SourceWorkItemType,TargetWorkItemType,TargetProject,TargetOrganisationUrl,TargetId\n"
                            + string.Join('\n', csvRows)
                            + '\n';
                        await context.Package.PersistContentAsync(
                            ContentAt("dependencies.csv"),
                            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(content))),
                            ct);
                    });

            return new DependencyAnalyser(factory.Object, orchestrator.Object, logger);
        }
    }

    private sealed record DependencyAnalysisResult(string? DependenciesCsv, IReadOnlyList<(LogLevel Level, string Message)> Logs)
    {
        public void ShouldProduceDependenciesCsvWithAtLeastOneDataRow()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(DependenciesCsv), "Expected analysis/dependencies.csv to be written.");
            var rowCount = DependenciesCsv!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            Assert.IsTrue(rowCount >= 1, "Expected analysis/dependencies.csv to contain at least one data row.");
        }

        public void ShouldEmitZeroOutputWarning()
        {
            Assert.IsTrue(
                Logs.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Zero cross-project dependency links", StringComparison.Ordinal)),
                "Expected dependency analysis warning when no cross-project links are found.");
        }
    }

    private sealed class InventoryAnalysisScenario
    {
        private bool _includeProjectInventory = true;

        public static InventoryAnalysisScenario WithAllInventoryCapableModulesEnabled() => new();

        public InventoryAnalysisScenario WithOneModuleInventoryMissing()
        {
            _includeProjectInventory = false;
            return this;
        }

        public async Task<InventoryAnalysisResult> RunAsync()
        {
            var package = PackageTestFactory.CreateLooseMock();
            var logger = new CapturingLogger<InventoryAnalyser>();
            var analyser = new InventoryAnalyser(logger);

            await WriteTextAsync(package.Object, "inventory.json", BuildRootInventoryJson());
            if (_includeProjectInventory)
            {
                await WriteTextAsync(package.Object, "testorg/ProjectA/inventory.json", BuildProjectInventoryJson());
            }

            await analyser.AnalyseAsync(
                new AnalyseContext
                {
                    Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
                    Package = package.Object
                },
                CancellationToken.None);

            var json = await ReadTextAsync(package.Object, "inventory.json");
            var csv = await ReadTextAsync(package.Object, "inventory.csv");
            return new InventoryAnalysisResult(json, csv, logger.Entries, _includeProjectInventory);
        }

        private static string BuildRootInventoryJson()
            => """
            {
              "generatedAt": "2024-01-01T00:00:00Z",
              "organisations": [
                {
                  "url": "https://dev.azure.com/testorg",
                  "totals": { "workItems": 0, "revisions": 0, "repos": 0, "projects": 1, "identities": 0, "nodes": 0, "teams": 0 },
                  "projects": [
                    { "name": "ProjectA", "workItems": 0, "revisions": 0, "repos": 0, "identities": 0, "nodes": 0, "teams": 0, "isComplete": true }
                  ]
                }
              ],
              "totals": { "workItems": 0, "revisions": 0, "repos": 0, "projects": 1, "identities": 0, "nodes": 0, "teams": 0 }
            }
            """;

        private static string BuildProjectInventoryJson()
            => """
            {
              "orgUrl": "https://dev.azure.com/testorg",
              "project": "ProjectA",
              "workItems": 5,
              "revisions": 0,
              "repos": 0,
              "identities": 4,
              "nodes": 3,
              "teams": 2,
              "isComplete": true
            }
            """;
    }

    private sealed record InventoryAnalysisResult(
        string? InventoryJson,
        string? InventoryCsv,
        IReadOnlyList<(LogLevel Level, string Message)> Logs,
        bool IncludesProjectInventory)
    {
        public void ShouldWriteRootInventoryJson()
            => Assert.IsFalse(string.IsNullOrWhiteSpace(InventoryJson), "Expected inventory.json to be written at package root.");

        public void ShouldWriteInventoryCsvWithAtLeastOneDataRow()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(InventoryCsv), "Expected inventory.csv to be written at package root.");
            var rowCount = InventoryCsv!.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            Assert.IsTrue(rowCount >= 1, "Expected inventory.csv to contain at least one data row.");
        }

        public void ShouldEmitMissingInventoryWarning()
        {
            Assert.IsFalse(IncludesProjectInventory, "This assertion expects the missing-module scenario.");
            Assert.IsTrue(
                Logs.Any(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Zero consolidated inventory total", StringComparison.Ordinal)),
                "Expected inventory warning when module inventory data is missing.");
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FixedEndpointOptions(string type) : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint()
            => new()
            {
                Type = type,
                ResolvedUrl = "https://org.example"
            };
    }

    private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

    private static PackageContentContext ContentAt(string path)
        => new(PackageContentKind.Artefact, "test-org", "test-project", "TestModule", Address: new TestPackageAddress(path));

    private static OrganisationsAnalyseContext CreateDependencyContext(string connectorType, IPackageAccess package)
        => new()
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Dependencies },
            Package = package,
            Organisations =
            [
                new ScopedOrganisationEndpoint
                {
                    Endpoint = new FixedEndpointOptions(connectorType) { Type = connectorType },
                    Projects = []
                }
            ]
        };

    private static async Task<string?> ReadTextAsync(IPackageAccess package, string path)
    {
        var payload = await package.RequestContentAsync(ContentAt(path), CancellationToken.None);
        if (payload is null)
            return null;

        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync();
    }

    private static async Task WriteTextAsync(IPackageAccess package, string path, string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistContentAsync(ContentAt(path), new PackagePayload(stream), CancellationToken.None);
    }
}
