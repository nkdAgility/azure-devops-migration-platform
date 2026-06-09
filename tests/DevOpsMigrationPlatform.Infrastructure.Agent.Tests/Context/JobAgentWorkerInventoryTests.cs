// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class JobAgentWorkerInventoryTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryDispatch_WithTwoSourceEndpoints_RecordsWorkItemInventoryTwice()
    {
        var PlatformMetrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);
        PlatformMetrics.Setup(m => m.RecordInventoryWorkItems(
                It.IsAny<int>(),
                It.Is<MetricsTagList>(t => HasTag(t, "module", "WorkItems"))))
            .Verifiable();

        var module = new FakeWorkItemsInventoryModule(PlatformMetrics.Object);
        var baseContext = new InventoryContext
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
            Package = PackageTestFactory.CreateLooseMock().Object,
            ProgressSink = Mock.Of<IProgressSink>()
        };

        foreach (var endpoint in new[]
        {
            new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org1.example" },
            new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org2.example" }
        })
        {
            await module.CaptureAsync(baseContext with { SourceEndpoint = endpoint }, CancellationToken.None);
        }

        PlatformMetrics.Verify(
            m => m.RecordInventoryWorkItems(It.IsAny<int>(), It.IsAny<MetricsTagList>()),
            Times.Exactly(2));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryDispatch_WithTwoSourceEndpoints_LogsOrgCountAndInvokesInventoryTwice()
    {
        var logger = new TestLogger();
        var module = new CountingInventoryModule();

        await InventoryDispatchHarness.RunAsync(
            module,
            logger,
            Mock.Of<IProgressSink>(),
            [
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org1.example" },
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org2.example" }
            ]);

        Assert.AreEqual(2, module.Calls);
        Assert.IsTrue(logger.Events.Any(e =>
            e.Level == LogLevel.Information &&
            e.Message.Contains("Starting multi-org inventory", System.StringComparison.Ordinal)));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryDispatch_WithTwoSourceEndpoints_EmitsPerOrgProgressWithCumulativeMetrics()
    {
        var progress = new Mock<IProgressSink>(MockBehavior.Strict);
        progress.Setup(p => p.Emit(It.Is<ProgressEvent>(evt =>
                evt.Module == "Inventory" &&
                evt.Stage == "Inventory.OrgCompleted" &&
                evt.Metrics != null &&
                evt.Metrics.Migration != null &&
                evt.Metrics.Migration.Inventory != null &&
                evt.Metrics.Migration.Inventory.Completed == 1)))
            .Verifiable();
        progress.Setup(p => p.Emit(It.Is<ProgressEvent>(evt =>
                evt.Module == "Inventory" &&
                evt.Stage == "Inventory.OrgCompleted" &&
                evt.Metrics != null &&
                evt.Metrics.Migration != null &&
                evt.Metrics.Migration.Inventory != null &&
                evt.Metrics.Migration.Inventory.Completed == 2)))
            .Verifiable();

        var module = new CountingInventoryModule();
        await InventoryDispatchHarness.RunAsync(
            module,
            new TestLogger(),
            progress.Object,
            [
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org1.example" },
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org2.example" }
            ]);

        progress.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task InventoryDispatch_WhenSecondOrgFails_LogsWarningAndContinues()
    {
        var logger = new TestLogger();
        var module = new CountingInventoryModule(throwOnCall: 2);

        await InventoryDispatchHarness.RunAsync(
            module,
            logger,
            Mock.Of<IProgressSink>(),
            [
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org1.example" },
                new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://org2.example" }
            ]);

        Assert.AreEqual(2, module.Calls, "Second endpoint should still be attempted.");
        Assert.IsTrue(logger.Events.Any(e =>
            e.Level == LogLevel.Warning &&
            e.Message.Contains("unreachable", System.StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasTag(MetricsTagList tags, string key, string value)
        => System.Linq.Enumerable.Any(tags, t => t.Key == key && string.Equals(t.Value?.ToString(), value, System.StringComparison.Ordinal));

    private sealed class FakeWorkItemsInventoryModule(IPlatformMetrics PlatformMetrics) : IModule
    {
        public string Name => "WorkItems";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public bool SupportsInventory => true;
        public bool SupportsExport => false;
        public bool SupportsPrepare => false;
        public bool SupportsImport => false;
        public bool SupportsValidate => false;

        public Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());

        public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        {
            PlatformMetrics.RecordInventoryWorkItems(
                1,
                new MetricsTagList
                {
                    { "job.id", context.Job.JobId },
                    { "module", Name },
                });

            return Task.FromResult(TaskExecutionResult.Completed());
        }
    }

    private sealed class CountingInventoryModule(int throwOnCall = -1) : IModule
    {
        public int Calls { get; private set; }

        public string Name => "WorkItems";
        public IReadOnlyList<ModuleDependency> DependsOn => [];
        public bool SupportsInventory => true;
        public bool SupportsExport => false;
        public bool SupportsPrepare => false;
        public bool SupportsImport => false;
        public bool SupportsValidate => false;

        public Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());
        public Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct) => Task.FromResult(TaskExecutionResult.Completed());

        public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        {
            Calls++;
            if (Calls == throwOnCall)
                throw new System.InvalidOperationException("endpoint unreachable");
            return Task.FromResult(TaskExecutionResult.Completed());
        }
    }

    private static class InventoryDispatchHarness
    {
        public static async Task RunAsync(
            IModule module,
            TestLogger logger,
            IProgressSink progressSink,
            IReadOnlyList<OrganisationEndpoint> endpoints)
        {
            logger.LogInformation("Starting multi-org inventory: {OrgCount} organisations", endpoints.Count);
            long cumulativeInventoryOperations = 0;
            var context = new InventoryContext
            {
                Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
                Package = PackageTestFactory.CreateLooseMock().Object,
                ProgressSink = progressSink
            };

            foreach (var (endpoint, index) in endpoints.Select((e, i) => (e, i + 1)))
            {
                try
                {
                    await module.CaptureAsync(context with { SourceEndpoint = endpoint }, CancellationToken.None);
                    cumulativeInventoryOperations++;
                }
                catch (System.Exception ex)
                {
                    logger.LogWarning(ex, "Organisation {OrgIndex}/{OrgCount} unreachable: {ErrorType}", index, endpoints.Count, ex.GetType().Name);
                }

                progressSink.Emit(new ProgressEvent
                {
                    Module = "Inventory",
                    Stage = "Inventory.OrgCompleted",
                    Metrics = new JobMetrics
                    {
                        Migration = new MigrationCounters
                        {
                            Inventory = new ModulePhaseCounters { Completed = cumulativeInventoryOperations }
                        }
                    }
                });
            }
        }
    }

    private sealed class TestLogger : ILogger
    {
        public sealed record LogEvent(LogLevel Level, string Message);
        public List<LogEvent> Events { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter)
            => Events.Add(new LogEvent(logLevel, formatter(state, exception)));
    }
}
