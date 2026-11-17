// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// #169: when the Control Plane fails a job before it ran (no capable agent, or a failed
/// dependency), the <c>job-failed</c> stream event carries the reason, and the CLI must show
/// that reason rather than a generic message, in every display mode.
/// </summary>
[TestClass]
public sealed class ControlPlaneClientFailureReasonTests
{
    private static ControlPlaneClient ClientServing(string ssePayload)
    {
        var http = new HttpClient(new DelegatingHandlerStub(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
        }))
        { BaseAddress = new Uri("http://localhost:5100") };
        return new ControlPlaneClient(http, NullLogger<ControlPlaneClient>.Instance);
    }

    private static async Task<JobStreamEvent> TerminalOf(ControlPlaneClient client)
    {
        await foreach (var e in client.StreamJobAsync(Guid.NewGuid(), CancellationToken.None))
            if (e.Kind == JobStreamEventKind.Terminal)
                return e;
        throw new AssertFailedException("No terminal event.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task StreamJobAsync_JobFailedWithReason_ReturnsThatReason()
    {
        var client = ClientServing(string.Join('\n',
            "event: job-failed",
            "data: {\"reason\":\"No connected agent can run Export job 1: start TfsMigrationAgent.\"}",
            string.Empty));

        var terminal = await TerminalOf(client);

        Assert.IsTrue(terminal.Failed);
        Assert.AreEqual("No connected agent can run Export job 1: start TfsMigrationAgent.", terminal.FailureReason);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task StreamJobAsync_JobFailedWithoutReason_KeepsGenericMessage()
    {
        var client = ClientServing(string.Join('\n', "event: job-failed", "data: {}", string.Empty));

        var terminal = await TerminalOf(client);

        Assert.IsTrue(terminal.Failed);
        Assert.AreEqual("Job failed. The reason is in the messages above.", terminal.FailureReason);
    }

    private sealed class DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
