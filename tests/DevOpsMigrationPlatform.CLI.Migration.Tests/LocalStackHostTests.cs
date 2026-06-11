// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests;

[TestClass]
public class LocalStackHostTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task WaitForHealthyAsync_RetriesHealthEndpointUntilItSucceeds()
    {
        var requestedPaths = new List<string>();
        var responses = new Queue<Func<HttpResponseMessage>>();
        responses.Enqueue(() => throw new HttpRequestException("not ready"));
        responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        responses.Enqueue(() => new HttpResponseMessage(HttpStatusCode.OK));

        using var http = new HttpClient(new DelegatingHandlerStub(request =>
        {
            requestedPaths.Add(request.RequestUri!.AbsolutePath);
            return responses.Dequeue().Invoke();
        }))
        {
            BaseAddress = new Uri("http://localhost:5101")
        };

        await LocalStackHost.WaitForHealthyAsync(
            http,
            new Uri("http://localhost:5101"),
            controlPlaneHasExited: () => false,
            readyTimeout: TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(
            new[] { "/health", "/health", "/health" },
            requestedPaths,
            "Standalone readiness should probe the health endpoint until it succeeds.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task WaitForHealthyAsync_ThrowsWhenProcessExitsBeforeHealthy()
    {
        using var http = new HttpClient(new DelegatingHandlerStub(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))
        {
            BaseAddress = new Uri("http://localhost:5101")
        };

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            LocalStackHost.WaitForHealthyAsync(
                http,
                new Uri("http://localhost:5101"),
                controlPlaneHasExited: () => true,
                readyTimeout: TimeSpan.FromSeconds(2)));

        StringAssert.Contains(ex.Message, "5101");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task EnsureProcessStaysAliveDuringStartupAsync_ThrowsWithCapturedOutputWhenProcessExitsEarly()
    {
        var exited = Task.FromResult(42);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            LocalStackHost.EnsureProcessStaysAliveDuringStartupAsync(
                processName: "MigrationAgent",
                exitedTask: exited,
                recentOutputProvider: _ => "stderr: fatal startup error",
                startupWindow: TimeSpan.FromSeconds(2)));

        StringAssert.Contains(ex.Message, "42");
        StringAssert.Contains(ex.Message, "fatal startup error");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task EnsureProcessStaysAliveDuringStartupAsync_DoesNotThrowWhenProcessSurvivesStartupWindow()
    {
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        await LocalStackHost.EnsureProcessStaysAliveDuringStartupAsync(
            processName: "MigrationAgent",
            exitedTask: exited.Task,
            recentOutputProvider: _ => string.Empty,
            startupWindow: TimeSpan.FromMilliseconds(100));

        Assert.IsFalse(exited.Task.IsCompleted);
    }

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}