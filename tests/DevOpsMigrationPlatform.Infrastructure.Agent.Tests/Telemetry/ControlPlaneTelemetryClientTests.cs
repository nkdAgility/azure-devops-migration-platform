// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class ControlPlaneTelemetryClientTests
{
    private MockHttpMessageHandler _handler = null!;
    private HttpClient _httpClient = null!;
    private ControlPlaneTelemetryClient _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _handler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("http://localhost:5100") };
        _sut = new ControlPlaneTelemetryClient(
            _httpClient,
            NullLogger<ControlPlaneTelemetryClient>.Instance);
    }

    [TestCleanup]
    public void Cleanup() => _httpClient.Dispose();

    // TODO: [test-validity] LOW VALUE — only asserts no exception on 404; covered by RequestBodyContainsValidJobMetrics which also uses 204
    [TestMethod]
    public async Task PushMetricsAsync_WhenServerReturns404_LogsWarningAndDoesNotThrow()
    {
        _handler.RespondWith(HttpStatusCode.NotFound);

        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 5 }
            }
        };
        await _sut.PushMetricsAsync("lease-404", metrics, CancellationToken.None);

        // No exception = test passes (warning is logged internally).
    }

    [TestMethod]
    public async Task PushMetricsAsync_RequestBodyContainsValidJobMetrics()
    {
        _handler.RespondWith(HttpStatusCode.NoContent);

        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters
                {
                    Attempted = 42,
                    Completed = 40,
                    Failed = 2
                }
            }
        };

        await _sut.PushMetricsAsync("lease-body", metrics, CancellationToken.None);

        Assert.IsNotNull(_handler.LastRequestContent);
        var deserialized = await _handler.LastRequestContent!
            .ReadFromJsonAsync<JobMetrics>();

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(42, deserialized!.Migration!.WorkItems.Attempted);
        Assert.AreEqual(40, deserialized!.Migration!.WorkItems.Completed);
    }

    // TODO: [test-validity] LOW VALUE — only asserts no exception on network failure; no assertion on logged warning or retry behaviour
    [TestMethod]
    public async Task PushMetricsAsync_WhenNetworkFails_DoesNotThrow()
    {
        _handler.ThrowOnSend(new HttpRequestException("connection refused"));

        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 1 }
            }
        };
        await _sut.PushMetricsAsync("lease-err", metrics, CancellationToken.None);

        // No exception = test passes.
    }
}

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> stub for unit tests.
/// </summary>
internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.NoContent;
    private Exception? _exceptionToThrow;

    public HttpContent? LastRequestContent { get; private set; }

    public void RespondWith(HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
        _exceptionToThrow = null;
    }

    public void ThrowOnSend(Exception ex)
    {
        _exceptionToThrow = ex;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequestContent = request.Content;

        // Force read so the content buffer is available after the request is disposed.
        if (request.Content != null)
            await request.Content.LoadIntoBufferAsync(cancellationToken);

        if (_exceptionToThrow != null)
            throw _exceptionToThrow;

        return new HttpResponseMessage(_statusCode);
    }
}
