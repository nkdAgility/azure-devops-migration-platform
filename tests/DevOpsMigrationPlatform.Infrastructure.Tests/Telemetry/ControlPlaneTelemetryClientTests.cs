using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
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

    [TestMethod]
    public async Task PushSnapshotAsync_WhenServerReturns204_DoesNotThrow()
    {
        _handler.RespondWith(HttpStatusCode.NoContent);

        var snapshot = new MetricSnapshot { WorkItemsAttempted = 10 };
        await _sut.PushSnapshotAsync("lease-1", snapshot, CancellationToken.None);

        // No exception = test passes.
    }

    [TestMethod]
    public async Task PushSnapshotAsync_WhenServerReturns404_LogsWarningAndDoesNotThrow()
    {
        _handler.RespondWith(HttpStatusCode.NotFound);

        var snapshot = new MetricSnapshot { WorkItemsAttempted = 5 };
        await _sut.PushSnapshotAsync("lease-404", snapshot, CancellationToken.None);

        // No exception = test passes (warning is logged internally).
    }

    [TestMethod]
    public async Task PushSnapshotAsync_RequestBodyContainsValidMetricSnapshot()
    {
        _handler.RespondWith(HttpStatusCode.NoContent);

        var snapshot = new MetricSnapshot
        {
            WorkItemsAttempted = 42,
            WorkItemsCompleted = 40,
            WorkItemsFailed = 2
        };

        await _sut.PushSnapshotAsync("lease-body", snapshot, CancellationToken.None);

        Assert.IsNotNull(_handler.LastRequestContent);
        var deserialized = await _handler.LastRequestContent!
            .ReadFromJsonAsync<MetricSnapshot>();

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(42, deserialized!.WorkItemsAttempted);
        Assert.AreEqual(40, deserialized!.WorkItemsCompleted);
    }

    [TestMethod]
    public async Task PushSnapshotAsync_WhenNetworkFails_DoesNotThrow()
    {
        _handler.ThrowOnSend(new HttpRequestException("connection refused"));

        var snapshot = new MetricSnapshot { WorkItemsAttempted = 1 };
        await _sut.PushSnapshotAsync("lease-err", snapshot, CancellationToken.None);

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
