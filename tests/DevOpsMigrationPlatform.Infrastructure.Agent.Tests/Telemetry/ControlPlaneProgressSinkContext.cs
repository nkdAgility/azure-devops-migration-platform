// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

internal sealed class ControlPlaneProgressSinkContext : IDisposable
{
    public ActiveLeaseState LeaseState { get; } = new() { CurrentLeaseId = "test-lease-001" };
    public List<string> CapturedRequestBodies { get; } = new();
    public List<string> DebugLogs { get; } = new();
    public HttpStatusCode NextResponseStatus { get; set; } = HttpStatusCode.NoContent;
    public bool ThrowHttpException { get; set; }

    public IHttpClientFactory BuildHttpClientFactory()
    {
        var handler = new CaptureHandler(this);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5100") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ControlPlaneProgressSink.HttpClientName)).Returns(client);
        return factory.Object;
    }

    public void Dispose() { }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly ControlPlaneProgressSinkContext _ctx;
        public CaptureHandler(ControlPlaneProgressSinkContext ctx) => _ctx = ctx;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (_ctx.ThrowHttpException)
                throw new HttpRequestException("Simulated transient failure");

            var body = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult() ?? string.Empty;
            _ctx.CapturedRequestBodies.Add(body);

            return Task.FromResult(new HttpResponseMessage(_ctx.NextResponseStatus));
        }
    }
}
