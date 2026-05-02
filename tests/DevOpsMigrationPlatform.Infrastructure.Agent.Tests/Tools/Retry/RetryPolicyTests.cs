// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.Retry;

/// <summary>
/// Unit tests for <see cref="AzureDevOpsRetryPolicy"/>.
/// Validates that the retry policies fire on 429/5xx responses and exhaust on repeated failure.
/// </summary>
[TestClass]
[TestCategory("UnitTest")]
public sealed class RetryPolicyTests
{
    // ─── HTTP policy (returns HttpResponseMessage) ───────────────────────────

    [TestMethod]
    public async Task HttpRetryPolicy_RetriesOnce_WhenFirst429ThenSuccess()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetRetryPolicy();

        // Act
        var response = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, attemptCount, "Expected exactly 2 attempts (1 failure + 1 success).");
    }

    [TestMethod]
    public async Task HttpRetryPolicy_RetriesThreeTimes_WhenConsecutive5xx()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetRetryPolicy();

        // Act — all 4 attempts (1 + 3 retries) fail with 503
        var response = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        });

        // Assert: policy exhausted retries but does NOT throw; returns last response
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.AreEqual(4, attemptCount, "Expected 1 initial + 3 retries = 4 attempts total.");
    }

    [TestMethod]
    public async Task HttpRetryPolicy_DoesNotRetry_On200()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetRetryPolicy();

        // Act
        var response = await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        // Assert: no retries on success
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(1, attemptCount, "No retries should occur on a 200 OK response.");
    }

    // ─── SDK policy (exception-based) ────────────────────────────────────────

    [TestMethod]
    public async Task SdkRetryPolicy_RetriesOnce_WhenFirst429ExceptionThenSuccess()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetSdkRetryPolicy();
        var resultValue = -1;

        // Act
        await policy.ExecuteAsync(() =>
        {
            attemptCount++;
            if (attemptCount == 1)
                throw new InvalidOperationException("HTTP 429 Too Many Requests");
            resultValue = 42;
            return Task.CompletedTask;
        });

        // Assert
        Assert.AreEqual(42, resultValue);
        Assert.AreEqual(2, attemptCount, "Expected exactly 2 attempts.");
    }

    [TestMethod]
    public async Task SdkRetryPolicy_Throws_AfterExhaustingRetries()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetSdkRetryPolicy();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await policy.ExecuteAsync(() =>
            {
                attemptCount++;
                throw new InvalidOperationException("ServiceUnavailable after 3 retries");
            }));

        Assert.AreEqual(4, attemptCount, "Expected 1 initial + 3 retries = 4 attempts before throwing.");
    }

    [TestMethod]
    public async Task SdkRetryPolicy_DoesNotRetry_OnUnrelatedExceptions()
    {
        // Arrange
        var attemptCount = 0;
        var policy = AzureDevOpsRetryPolicy.GetSdkRetryPolicy();

        // Act & Assert — ArgumentException is not a retryable exception
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await policy.ExecuteAsync(() =>
            {
                attemptCount++;
                throw new ArgumentException("Not a transient error");
            }));

        Assert.AreEqual(1, attemptCount, "Non-retryable exceptions should not trigger retry.");
    }
}
