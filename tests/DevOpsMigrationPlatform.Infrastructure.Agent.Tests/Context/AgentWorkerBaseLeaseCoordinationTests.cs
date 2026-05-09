// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using System.Net.Http.Json;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentWorkerBaseLeaseCoordinationTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState()
    {
        var leaseState = new ActiveLeaseState();
        var packageState = new ActivePackageState();
        var job = new Job
        {
            JobId = "job-dispatch-failure",
            Kind = JobKind.Export,
            Package = new JobPackage { PackageUri = "file:///tmp/package" }
        };

        using var client = new HttpClient(new SingleLeaseResponseHandler("lease-dispatch-failure", job))
        {
            BaseAddress = new Uri("http://localhost:5100")
        };
        var worker = new ThrowingAgentWorker(
            leaseState,
            packageState,
            new TestHttpClientFactory(client));

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await worker.StartAsync(runCts.Token);

        var observedDispatch = await Task.WhenAny(
            worker.DispatchObserved.Task,
            Task.Delay(TimeSpan.FromSeconds(2), runCts.Token));

        Assert.AreSame(worker.DispatchObserved.Task, observedDispatch, "The leased job should be dispatched before assertions run.");
        Assert.AreEqual("lease-dispatch-failure", await worker.DispatchObserved.Task);
        Assert.AreEqual(1, worker.DispatchCount);

        await runCts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.IsNull(leaseState.CurrentLeaseId, "The active lease must be cleared even when job dispatch throws.");
        Assert.IsNull(packageState.CurrentJob, "The active job must be cleared even when job dispatch throws.");
        Assert.IsNull(packageState.CurrentRunId, "The cached run id must be cleared with the active package state.");
    }

    private sealed class ThrowingAgentWorker(
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory)
        : AgentWorkerBase(
            leaseState,
            packageState,
            httpClientFactory,
            NullLogger<ThrowingAgentWorker>.Instance)
    {
        private readonly ActivePackageState _packageState = packageState;
        private readonly TaskCompletionSource<string> _dispatchObserved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<string> DispatchObserved => _dispatchObserved;
        public int DispatchCount { get; private set; }

        protected override ConnectorType[] Capabilities => [ConnectorType.Simulated];

        protected override Task OnJobAsync(Job job, HttpClient controlPlane, string leaseId, CancellationToken ct)
        {
            DispatchCount++;
            _ = _packageState.CurrentRunId;
            _dispatchObserved.TrySetResult(leaseId);
            throw new InvalidOperationException("Deterministic dispatch failure for lease cleanup test.");
        }
    }

    private sealed class SingleLeaseResponseHandler(string leaseId, Job job) : HttpMessageHandler
    {
        private bool _leaseReturned;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_leaseReturned && request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/agents/lease")
            {
                _leaseReturned = true;
                return Task.FromResult(HttpResponseMessageJson(new LeaseEnvelope(leaseId, job)));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        private static HttpResponseMessage HttpResponseMessageJson(LeaseEnvelope lease)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = JsonContent.Create(lease);
            return response;
        }
    }

    private sealed record LeaseEnvelope(string LeaseId, Job Job);

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
