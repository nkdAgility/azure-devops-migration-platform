// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using System.Net.Http.Json;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentWorkerBaseLeaseCoordinationTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState()
    {
        var leaseState = new ActiveLeaseState();
        var packageState = new ActivePackageState();
        var job = new Job
        {
            JobId = "job-dispatch-failure",
            Kind = JobKind.Export,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\"file:///tmp/package\"}}}"
        };

        using var client = new HttpClient(new SingleLeaseResponseHandler("lease-dispatch-failure", job))
        {
            BaseAddress = new Uri("http://localhost:5100")
        };
        var httpClientFactory = new TestHttpClientFactory(client);
        var eventWriter = new UnifiedWorkerEventWriter(
            httpClientFactory,
            leaseState,
            NullLogger<UnifiedWorkerEventWriter>.Instance);
        var worker = new ThrowingAgentWorker(
            leaseState,
            packageState,
            httpClientFactory,
            eventWriter);

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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExecuteAsync_WhenJobDispatchThrows_ReportsTheJobFailed()
    {
        // #171 live run: a TFS Export threw while starting; nothing told the Control Plane, so the
        // job stayed leased for 16 hours and Prepare and Import waited behind it for ever.
        var leaseState = new ActiveLeaseState();
        var packageState = new ActivePackageState();
        var job = new Job
        {
            JobId = "job-dispatch-failure",
            Kind = JobKind.Export,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\"file:///tmp/package\"}}}"
        };
        var handler = await RunThrowingJob(new InvalidOperationException("Deterministic dispatch failure."));

        var terminal = handler.Events.SingleOrDefault(e => e.Kind == "Terminal");
        Assert.IsNotNull(terminal, "A terminal event must reach the Control Plane.");
        // PR #222 review: check failed on the terminal event itself, and the unified route.
        StringAssert.Contains(terminal.PayloadJson, "\"failed\":true");
        Assert.AreEqual("lease-dispatch-failure", terminal.LeaseId);
        StringAssert.Matches(terminal.Path, new System.Text.RegularExpressions.Regex("^/workers/[^/]+/events$"));
    }

    // PR #222 review: the failed terminal carries only a flag, so the operator must get the
    // reason as a progress message first: the configuration message when it is one, a generic
    // message otherwise, never raw exception text.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExecuteAsync_JobConfigurationError_SendsItsMessageBeforeTheTerminal()
    {
        var handler = await RunThrowingJob(new DevOpsMigrationPlatform.Abstractions.Agent.Context.JobConfigurationException(
            "A migration job has one source project; configure only one."));

        Assert.IsTrue(handler.Events.Any(e => e.Kind == "Progress" && e.PayloadJson.Contains("configure only one")),
            "The configuration message must reach the job stream.");
        Assert.IsTrue(handler.Events.FindIndex(e => e.Kind == "Progress" && e.PayloadJson.Contains("configure only one"))
                      < handler.Events.FindIndex(e => e.Kind == "Terminal"));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ExecuteAsync_OtherError_SendsAGenericMessageWithoutTheExceptionText()
    {
        var handler = await RunThrowingJob(new InvalidOperationException("internal detail token=abc"));

        var failed = handler.Events.Where(e => e.Kind == "Progress" && e.PayloadJson.Contains("Job.Failed")).ToList();
        Assert.IsTrue(failed.Count > 0, "A failure reason must reach the job stream.");
        Assert.IsFalse(handler.Events.Any(e => e.PayloadJson.Contains("internal detail")), "Raw exception text must not be sent.");
    }

    private static async Task<SingleLeaseResponseHandler> RunThrowingJob(Exception failure)
    {
        var leaseState = new ActiveLeaseState();
        var packageState = new ActivePackageState();
        var job = new Job
        {
            JobId = "job-dispatch-failure",
            Kind = JobKind.Export,
            ConfigPayload = "{\"MigrationPlatform\":{\"Package\":{\"WorkingDirectory\":\"file:///tmp/package\"}}}"
        };
        var handler = new SingleLeaseResponseHandler("lease-dispatch-failure", job);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var httpClientFactory = new TestHttpClientFactory(client);
        var eventWriter = new UnifiedWorkerEventWriter(httpClientFactory, leaseState, NullLogger<UnifiedWorkerEventWriter>.Instance);
        var worker = new ThrowingAgentWorker(leaseState, packageState, httpClientFactory, eventWriter, failure);

        using var runCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StartAsync(runCts.Token);
        await Task.WhenAny(handler.TerminalPosted.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        await runCts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);
        return handler;
    }

    private sealed class ThrowingAgentWorker(
        ActiveLeaseState leaseState,
        ActivePackageState packageState,
        IHttpClientFactory httpClientFactory,
        UnifiedWorkerEventWriter eventWriter,
        Exception? failure = null)
        : AgentWorkerBase(
            leaseState,
            packageState,
            httpClientFactory,
            NullLogger<ThrowingAgentWorker>.Instance,
            eventWriter)
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
            throw failure ?? new InvalidOperationException("Deterministic dispatch failure for lease cleanup test.");
        }
    }

    private sealed class SingleLeaseResponseHandler(string leaseId, Job job) : HttpMessageHandler
    {
        private bool _leaseReturned;

        /// <summary>Every worker event posted, in order.</summary>
        public List<PostedEvent> Events { get; } = new();

        /// <summary>Completes when a terminal event is posted.</summary>
        public TaskCompletionSource<string> TerminalPosted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.Content is not null)
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(body);
                var leaseId = doc.RootElement.TryGetProperty("leaseId", out var l) ? l.GetString() : null;
                foreach (var evt in doc.RootElement.GetProperty("events").EnumerateArray())
                {
                    var kind = evt.GetProperty("kind").GetString() ?? string.Empty;
                    lock (Events)
                        Events.Add(new PostedEvent(request.RequestUri!.AbsolutePath, leaseId, kind, evt.GetProperty("payloadJson").GetString() ?? string.Empty));
                    if (kind == "Terminal")
                        TerminalPosted.TrySetResult(body);
                }
                return new HttpResponseMessage(HttpStatusCode.Accepted);
            }
            return await Respond(request);
        }

        private Task<HttpResponseMessage> Respond(HttpRequestMessage request)
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

    private sealed record PostedEvent(string Path, string? LeaseId, string Kind, string PayloadJson);

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
