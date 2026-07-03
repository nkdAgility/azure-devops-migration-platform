// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

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
