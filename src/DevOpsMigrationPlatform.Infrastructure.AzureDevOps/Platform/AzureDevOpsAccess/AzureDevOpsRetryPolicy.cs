// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;

/// <summary>
/// Provides a shared Polly-based HTTP retry policy for Azure DevOps REST API clients.
/// Handles 429 (Too Many Requests), 5xx server errors, and 408 (Request Timeout)
/// with exponential back-off.
/// </summary>
public static class AzureDevOpsRetryPolicy
{
    /// <summary>
    /// Returns an async Polly policy that retries on transient HTTP failures (5xx, 408, 429).
    /// Uses exponential back-off: 1s, 2s, 4s.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()                          // 5xx + 408
            .OrResult(r => (int)r.StatusCode == 429)            // Too Many Requests
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (_, timespan, retryCount, _) =>
                {
                    // Logging handled by the calling service
                    _ = retryCount;
                    _ = timespan;
                });
    }

    /// <summary>
    /// Returns an async Polly policy that wraps a generic async operation with
    /// exception-based retry (no HTTP response available — e.g. SDK-level exceptions).
    /// Retries on <see cref="System.TimeoutException"/> or any exception whose message
    /// contains "429" or "Too Many Requests".
    /// </summary>
    public static IAsyncPolicy GetSdkRetryPolicy(ILogger? logger = null)
    {
        return Policy
            .Handle<TimeoutException>()
            .Or<Exception>(ex =>
                ex.Message.Contains("429", StringComparison.Ordinal) ||
                ex.Message.Contains("Too Many Requests", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase))
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, timespan, retryCount, _) =>
                {
                    logger?.LogDebug(
                        ex,
                        "[ADO] Transient error on attempt {RetryCount}, retrying in {Delay:g}: {Message}",
                        retryCount, timespan, ex.Message);
                });
    }
}
