// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobAgentWorkerInventoryTests
{
    [TestMethod]
    public async Task InventoryDispatch_WithTwoSourceEndpoints_RecordsWorkItemInventoryTwice()
    {
        var discoveryMetrics = new Mock<IDiscoveryMetrics>(MockBehavior.Strict);
        discoveryMetrics.Setup(m => m.RecordInventoryWorkItems(
                It.IsAny<int>(),
                It.Is<MetricsTagList>(t => HasTag(t, "module", "WorkItems"))))
            .Verifiable();

        var module = new FakeWorkItemsInventoryModule(discoveryMetrics.Object);
        var baseContext = new InventoryContext
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
            ArtefactStore = Mock.Of<IArtefactStore>(),
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = Mock.Of<IProgressSink>()
        };

        foreach (var endpoint in new[]
        {
            new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org1.example" },
            new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org2.example" }
        })
        {
            await module.InventoryAsync(baseContext with { SourceEndpoint = endpoint }, CancellationToken.None);
        }

        discoveryMetrics.Verify(
            m => m.RecordInventoryWorkItems(It.IsAny<int>(), It.IsAny<MetricsTagList>()),
            Times.Exactly(2));
    }

    private static bool HasTag(MetricsTagList tags, string key, string value)
        => System.Linq.Enumerable.Any(tags, t => t.Key == key && string.Equals(t.Value?.ToString(), value, System.StringComparison.Ordinal));

    private sealed class FakeWorkItemsInventoryModule(IDiscoveryMetrics discoveryMetrics) : IModule
    {
        public string Name => "WorkItems";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public bool SupportsInventory => true;
        public bool SupportsExport => false;
        public bool SupportsPrepare => false;
        public bool SupportsImport => false;
        public Task ExportAsync(ExportContext context, CancellationToken ct) => Task.CompletedTask;
        public Task PrepareAsync(PrepareContext context, CancellationToken ct) => Task.CompletedTask;
        public Task ImportAsync(ImportContext context, CancellationToken ct) => Task.CompletedTask;
        public Task ValidateAsync(ValidationContext context, CancellationToken ct) => Task.CompletedTask;

        public Task InventoryAsync(InventoryContext context, CancellationToken ct)
        {
            discoveryMetrics.RecordInventoryWorkItems(
                1,
                new MetricsTagList
                {
                    { "job.id", context.Job.JobId },
                    { "module", Name },
                });

            return Task.CompletedTask;
        }
    }
}
