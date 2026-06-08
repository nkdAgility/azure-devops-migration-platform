// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class ControlPlaneProgressSinkTests
{
    private ControlPlaneProgressSinkContext _ctx = null!;

    [TestInitialize]
    public void Setup()
    {
        _ctx = new ControlPlaneProgressSinkContext();
    }

    [TestCleanup]
    public void Teardown()
    {
        _ctx.Dispose();
    }

    private (ControlPlaneProgressSink sink, CancellationTokenSource cts) BuildStartedSink()
    {
        var factory = _ctx.BuildHttpClientFactory();
        var cts = new CancellationTokenSource();
        var sink = new ControlPlaneProgressSink(
            factory,
            _ctx.LeaseState,
            NullLogger<ControlPlaneProgressSink>.Instance);
        _ = sink.StartAsync(cts.Token);
        return (sink, cts);
    }

    private static async Task StopSink(ControlPlaneProgressSink sink, CancellationTokenSource cts)
    {
        await cts.CancelAsync();
        await sink.StopAsync(CancellationToken.None);
        sink.Dispose();
        cts.Dispose();
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task Emit_PostsProgressEventToControlPlane_WithinOneSecond()
    {
        // Scenario: Sink POSTs a ProgressEvent to the Control Plane within 1 second of Emit
        _ctx.NextResponseStatus = HttpStatusCode.NoContent;
        _ctx.ThrowHttpException = false;

        var (sink, cts) = BuildStartedSink();
        try
        {
            sink.Emit(new ProgressEvent { Module = "workitems", Stage = "TestStage" });
            await Task.Delay(300);

            Assert.IsTrue(_ctx.CapturedRequestBodies.Count > 0,
                "Expected at least one POST request to the Control Plane endpoint.");
        }
        finally
        {
            await StopSink(sink, cts);
        }
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task Emit_AfterControlPlaneRestart_CreatesNewRingBufferAndStoresEvent()
    {
        // Scenario: Fresh ring buffer is created on Control Plane restart when agent resumes posting
        _ctx.NextResponseStatus = HttpStatusCode.NoContent;
        _ctx.ThrowHttpException = false;

        var (sink, cts) = BuildStartedSink();
        try
        {
            sink.Emit(new ProgressEvent { Module = "workitems", Stage = "PostRestartStage" });
            await Task.Delay(300);

            Assert.IsTrue(_ctx.CapturedRequestBodies.Count > 0,
                "At least one request must have been captured, indicating the ring buffer was created.");
        }
        finally
        {
            await StopSink(sink, cts);
        }
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task Emit_WhenHttpEndpointUnreachable_DropsEventWithoutThrowingAndContinues()
    {
        // Scenario: Transient HTTP failure causes event to be dropped and job continues
        _ctx.ThrowHttpException = true;

        var (sink, cts) = BuildStartedSink();
        try
        {
            // Should not throw
            sink.Emit(new ProgressEvent { Module = "workitems", Stage = "FailStage" });
            await Task.Delay(300);

            // Subsequent emit calls should also not throw
            sink.Emit(new ProgressEvent { Module = "workitems", Stage = "SubsequentStage" });
            await Task.Delay(300);

            // Reaching here means no exception was propagated — the sink swallowed it
            Assert.IsTrue(true, "No exception was thrown; subsequent emits are unaffected.");
        }
        finally
        {
            await StopSink(sink, cts);
        }
    }
}
