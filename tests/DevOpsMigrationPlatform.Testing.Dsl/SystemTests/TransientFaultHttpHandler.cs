// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Delegating handler that returns a scripted sequence of HTTP status codes.
/// First N responses use <see cref="TransientStatusCode"/>; subsequent responses succeed.
/// Designed for use with AzureDevOpsRetryPolicy tests.
/// </summary>
public sealed class TransientFaultHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _transientStatusCode;
    private readonly int _failureCount;
    private int _callCount;

    public TransientFaultHttpHandler(
        HttpStatusCode transientStatusCode = HttpStatusCode.ServiceUnavailable,
        int failureCount = 1)
    {
        _transientStatusCode = transientStatusCode;
        _failureCount = failureCount;
    }

    /// <summary>Total number of times SendAsync was invoked (including retries).</summary>
    public int TotalCallCount => _callCount;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        _callCount++;
        var statusCode = _callCount <= _failureCount
            ? _transientStatusCode
            : HttpStatusCode.OK;
        return Task.FromResult(new HttpResponseMessage(statusCode));
    }
}
