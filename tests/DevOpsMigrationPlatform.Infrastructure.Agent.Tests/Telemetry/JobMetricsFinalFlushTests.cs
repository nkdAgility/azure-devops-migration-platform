// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Telemetry;

/// <summary>
/// #171 live E2E: a job that ended between two metric export cycles reported its terminal state
/// before its final counts reached the Control Plane, so the CLI summary read 0 work items
/// imported. The pre-terminal flush now collects the metrics and pushes them first.
/// </summary>
[TestClass]
public sealed class JobMetricsFinalFlushTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task FlushAsync_CollectsThenPushesTheLatestMetrics()
    {
        var store = new Store();
        var leaseState = new ActiveLeaseState { CurrentLeaseId = "lease-1" };
        var handler = new RecordingHandler();
        var factory = new Factory(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") });
        var writer = new UnifiedWorkerEventWriter(factory, leaseState, NullLogger<UnifiedWorkerEventWriter>.Instance);
        // The collect step is what the OTel reader does on its cycle: it updates the store.
        var flush = new JobMetricsFinalFlush(store, writer,
            collect: () => store.Update(new JobMetrics { Migration = new MigrationCounters { WorkItems = new WorkItemCounters { Completed = 3 } } }));

        await flush.FlushAsync();

        var posted = string.Join("\n", handler.Bodies);
        StringAssert.Contains(posted, "\"kind\":\"Metrics\"");
        StringAssert.Contains(posted, "completed\\u0022:3");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void AddCoreAgentServices_RegistersTheFinalMetricsFlushAsAFlushable()
    {
        // Every agent flushes its IFlushable set before signalling the terminal state.
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddCoreAgentServices(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Uri("http://localhost:5100"));

        Assert.IsTrue(services.Any(d => d.ServiceType == typeof(JobMetricsFinalFlush)),
            "The final metrics flush must be registered so it runs before the terminal signal.");
    }

    private sealed class Store : IJobMetricsStore
    {
        public JobMetrics? Latest { get; private set; }
        public void Update(JobMetrics metrics) => Latest = metrics;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class Factory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
