// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class DependencyOrchestratorTests
{
    [TestMethod]
    public async Task AnalyseAsync_WritesMostRecentDateIntoGroupedProjectSummary()
    {
        var store = new InMemoryArtefactStore();
        var stateStore = new InMemoryStateStore();
        var service = new FakeDependencyDiscoveryService(
        [
            new DependencyFoundEvent(new DependencyRecord
            {
                SourceWorkItemId = 101,
                SourceWorkItemType = "Bug",
                SourceProject = "ProjectA",
                SourceOrganisationUrl = "https://dev.azure.com/org",
                LinkType = "Related",
                LinkScope = LinkScope.CrossProject,
                TargetWorkItemId = 202,
                TargetProject = "ProjectB",
                TargetOrganisation = string.Empty,
                TargetStatus = TargetStatus.Reachable,
                LinkChangedDate = new DateTimeOffset(2024, 03, 01, 10, 00, 00, TimeSpan.Zero),
                SourceWorkItemChangedDate = new DateTimeOffset(2024, 03, 05, 12, 30, 00, TimeSpan.Zero),
                SourceWorkItemStateCategory = "Active"
            }),
            new DependencyFoundEvent(new DependencyRecord
            {
                SourceWorkItemId = 102,
                SourceWorkItemType = "Task",
                SourceProject = "ProjectA",
                SourceOrganisationUrl = "https://dev.azure.com/org",
                LinkType = "Related",
                LinkScope = LinkScope.CrossProject,
                TargetWorkItemId = 203,
                TargetProject = "ProjectB",
                TargetOrganisation = string.Empty,
                TargetStatus = TargetStatus.Reachable,
                LinkChangedDate = new DateTimeOffset(2024, 04, 01, 08, 15, 00, TimeSpan.Zero),
                SourceWorkItemChangedDate = new DateTimeOffset(2024, 04, 02, 09, 45, 00, TimeSpan.Zero),
                SourceWorkItemStateCategory = "Closed"
            }),
            new DependencyHeartbeatEvent(
                "https://dev.azure.com/org",
                "ProjectA",
                2,
                2,
                2,
                0,
                true,
                TotalWorkItems: 2)
        ]);

        var orchestrator = new DependencyOrchestrator(
            NullLogger<DependencyOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/org", "ProjectA"),
            package: PackageTestFactory.CreateDelegatingMock(store, stateStore).Object);

        await orchestrator.AnalyseAsync(
            service,
            new OrganisationsAnalyseContext
            {
                Job = new Job { JobId = "job-1", Kind = JobKind.Dependencies },
                ArtefactStore = store,
                StateStore = stateStore,
                Organisations =
                [
                    new ScopedOrganisationEndpoint
                    {
                        Endpoint = new SimulatedEndpointOptions { Type = "Simulated", Url = "https://dev.azure.com/org" },
                        Projects = ["ProjectA"]
                    }
                ]
            },
            new JobPolicies { CheckpointIntervalSeconds = 300 },
            300,
            CancellationToken.None);

        var groupedCsv = await store.ReadAsync("discovery-project-dependencies.csv", CancellationToken.None);

        Assert.IsNotNull(groupedCsv);
        var lines = groupedCsv!.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(
            "SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId,MostRecentLinkDate,MostRecentSourceWorkItemChangedDate",
            lines[0]);
        StringAssert.Contains(lines[1], "2024-04-01T08:15:00.0000000+00:00");
        StringAssert.Contains(lines[1], "2024-04-02T09:45:00.0000000+00:00");
    }

    [TestMethod]
    public async Task CaptureProjectAsync_WhenBatchCheckpointOccurs_WritesProjectScopedDependencyState()
    {
        var store = new InMemoryArtefactStore();
        var stateStore = new RecordingStateStore();
        var service = new CheckpointingDependencyDiscoveryService();

        var orchestrator = new DependencyOrchestrator(
            NullLogger<DependencyOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/org", "ProjectA"),
            package: PackageTestFactory.CreateDelegatingMock(store, stateStore).Object);

        await orchestrator.CaptureProjectAsync(
            service,
            new InventoryContext
            {
                Job = new Job { JobId = "job-2", Kind = JobKind.Dependencies },
                ArtefactStore = store,
                StateStore = stateStore,
                SourceEndpoint = new OrganisationEndpoint
                {
                    ResolvedUrl = "https://dev.azure.com/org",
                    Type = "Simulated"
                },
                Project = "ProjectA",
                Organisations =
                [
                    new ScopedOrganisationEndpoint
                    {
                        Endpoint = new SimulatedEndpointOptions { Type = "Simulated", Url = "https://dev.azure.com/org" },
                        Projects = ["ProjectA"]
                    }
                ]
            },
            new JobPolicies { CheckpointIntervalSeconds = 0 },
            CancellationToken.None);

        var expectedCursorKey = PackagePathTestHelper.CursorFile("dependencies", "dependencies", "https://dev.azure.com/org", "ProjectA");
        var expectedContinuationKey = PackagePathTestHelper.ContinuationFile("dependencies", "dependencies", "https://dev.azure.com/org", "ProjectA");
        Assert.IsTrue(stateStore.WrittenKeys.Contains(expectedCursorKey), "Dependencies capture must checkpoint to the project-local cursor path.");
        Assert.IsTrue(stateStore.WrittenKeys.Contains(expectedContinuationKey), "Dependencies capture must persist the continuation token for project-local resume.");
        Assert.IsFalse(stateStore.WrittenKeys.Contains(PackagePathTestHelper.CursorFile("DependencyDiscovery")), "Dependencies capture must not write the legacy root dependency cursor.");

        var canonicalProjectCsv = await store.ReadAsync("org/ProjectA/dependencies.csv", CancellationToken.None);
        var invalidDiscoveryCsv = await store.ReadAsync("discovery/org/ProjectA/dependencies.csv", CancellationToken.None);
        Assert.IsNotNull(canonicalProjectCsv, "Dependencies capture must write to the canonical org/project path.");
        Assert.IsNull(invalidDiscoveryCsv, "Dependencies capture must not write to the invalid discovery/ subtree.");
    }

    [TestMethod]
    public async Task AnalyseAsync_DoesNotWriteLegacyAggregateDependencyCursor()
    {
        var store = new InMemoryArtefactStore();
        var stateStore = new RecordingStateStore();
        var service = new FakeDependencyDiscoveryService(
        [
            new DependencyHeartbeatEvent(
                "https://dev.azure.com/org",
                "ProjectA",
                3,
                1,
                1,
                0,
                false,
                TotalWorkItems: 10)
        ]);

        var orchestrator = new DependencyOrchestrator(
            NullLogger<DependencyOrchestrator>.Instance,
            CreateCheckpointingFactory("https://dev.azure.com/org", "ProjectA"),
            package: PackageTestFactory.CreateDelegatingMock(store, stateStore).Object);

        await orchestrator.AnalyseAsync(
            service,
            new OrganisationsAnalyseContext
            {
                Job = new Job { JobId = "job-aggregate", Kind = JobKind.Dependencies },
                ArtefactStore = store,
                StateStore = stateStore,
                Organisations =
                [
                    new ScopedOrganisationEndpoint
                    {
                        Endpoint = new SimulatedEndpointOptions { Type = "Simulated", Url = "https://dev.azure.com/org" },
                        Projects = ["ProjectA"]
                    }
                ]
            },
            new JobPolicies { CheckpointIntervalSeconds = 0 },
            0,
            CancellationToken.None);

        Assert.IsFalse(
            stateStore.WrittenKeys.Contains(PackagePathTestHelper.CursorFile("DependencyDiscovery")),
            "Aggregate dependency analysis must not persist the legacy root cursor blob in the clean long-term model.");
    }

    private sealed class FakeDependencyDiscoveryService : IDependencyDiscoveryService
    {
        private readonly IReadOnlyList<DependencyProgressEvent> _events;

        public FakeDependencyDiscoveryService(IReadOnlyList<DependencyProgressEvent> events)
        {
            _events = events;
        }

        public async IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
            HashSet<string>? completedProjectKeys = null,
            string? wiqlFilter = null,
            string? inProgressProjectKey = null,
            DevOpsMigrationPlatform.Abstractions.Agent.Export.BatchContinuationToken? inProgressToken = null,
            Func<DevOpsMigrationPlatform.Abstractions.Agent.Export.BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var dependencyProgressEvent in _events)
            {
                yield return dependencyProgressEvent;
                await Task.Yield();
            }
        }
    }

    private sealed class CheckpointingDependencyDiscoveryService : IDependencyDiscoveryService
    {
        public async IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(
            HashSet<string>? completedProjectKeys = null,
            string? wiqlFilter = null,
            string? inProgressProjectKey = null,
            BatchContinuationToken? inProgressToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (continuationCheckpointWriter is not null)
            {
                await continuationCheckpointWriter(
                    new BatchContinuationToken
                    {
                        ChangedDateUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        WorkItemId = 42,
                        QueryFingerprint = "fingerprint",
                        GeneratedAtUtc = new DateTime(2026, 1, 1, 0, 0, 1, DateTimeKind.Utc)
                    },
                    cancellationToken);
            }

            yield return new DependencyHeartbeatEvent(
                "https://dev.azure.com/org",
                "ProjectA",
                3,
                1,
                1,
                0,
                false,
                TotalWorkItems: 10);
        }
    }

    private sealed class InMemoryArtefactStore : IArtefactStore
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.TryGetValue(path, out var value) ? value : null);

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.ContainsKey(path));

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k, StringComparer.Ordinal))
            {
                yield return key;
                await Task.Yield();
            }
        }

        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = _files.TryGetValue(path, out var existing) ? existing + content : content;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryStateStore : IStateStore
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_entries.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            _entries.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStateStore : IStateStore
    {
        private readonly Dictionary<string, string> _entries = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> WrittenKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
        {
            WrittenKeys.Add(key);
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(_entries.ContainsKey(key));

        public Task DeleteAsync(string key, CancellationToken cancellationToken)
        {
            _entries.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static ICheckpointingServiceFactory CreateCheckpointingFactory(string endpointUrl, string projectName)
    {
        var endpointAccessor = new Mock<ICurrentJobEndpointAccessor>(MockBehavior.Strict);
        var sourceInfo = new Mock<ISourceEndpointInfo>(MockBehavior.Strict);
        sourceInfo.SetupGet(s => s.Url).Returns(endpointUrl);
        sourceInfo.SetupGet(s => s.Project).Returns(projectName);
        sourceInfo.SetupGet(s => s.ConnectorType).Returns("Simulated");
        endpointAccessor.SetupGet(a => a.Source).Returns(sourceInfo.Object);
        endpointAccessor.SetupGet(a => a.Target).Returns((ITargetEndpointInfo?)null);

        var packageConfigAccessor = new Mock<ICurrentPackageConfigAccessor>(MockBehavior.Strict);
        packageConfigAccessor.SetupGet(a => a.Current).Returns((Microsoft.Extensions.Configuration.IConfiguration?)null);

        return new TestCheckpointingServiceFactory(endpointAccessor.Object, packageConfigAccessor.Object);
    }

    private sealed class TestCheckpointingServiceFactory : ICheckpointingServiceFactory
    {
        private readonly ICurrentJobEndpointAccessor _endpointAccessor;
        private readonly ICurrentPackageConfigAccessor _packageConfigAccessor;

        public TestCheckpointingServiceFactory(
            ICurrentJobEndpointAccessor endpointAccessor,
            ICurrentPackageConfigAccessor packageConfigAccessor)
        {
            _endpointAccessor = endpointAccessor;
            _packageConfigAccessor = packageConfigAccessor;
        }

        public ICheckpointingService Create(IStateStore stateStore)
            => new CheckpointingService(
                stateStore,
                _endpointAccessor,
                _packageConfigAccessor,
                package: PackageTestFactory.CreateStateDelegatingMock(stateStore).Object);
    }
}
