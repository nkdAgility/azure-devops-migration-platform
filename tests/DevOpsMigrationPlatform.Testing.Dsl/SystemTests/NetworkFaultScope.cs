// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Fluent builder that wires TransientFaultHttpHandler into an HttpClient
/// for retry-policy testing.
/// </summary>
public sealed class NetworkFaultScope
{
    private HttpStatusCode _statusCode = HttpStatusCode.ServiceUnavailable;
    private int _failureCount = 1;

    private NetworkFaultScope() { }

    /// <summary>Creates a scope with default 503 on the first attempt.</summary>
    public static NetworkFaultScope WithOneTransientFailure()
        => new() { _failureCount = 1 };

    /// <summary>Configures the number of transient failures before success.</summary>
    public NetworkFaultScope FailingTimes(int count)
    {
        _failureCount = count;
        return this;
    }

    /// <summary>Configures the HTTP status code to return during transient failures.</summary>
    public NetworkFaultScope WithStatusCode(HttpStatusCode statusCode)
    {
        _statusCode = statusCode;
        return this;
    }

    /// <summary>
    /// Builds the fault handler and an HttpClient wired to it.
    /// Caller is responsible for disposing the client.
    /// </summary>
    public (HttpClient Client, TransientFaultHttpHandler Handler) Build()
    {
        var handler = new TransientFaultHttpHandler(_statusCode, _failureCount);
        var client = new HttpClient(handler);
        return (client, handler);
    }
}
