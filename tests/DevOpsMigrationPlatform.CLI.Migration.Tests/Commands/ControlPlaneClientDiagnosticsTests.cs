// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public sealed class ControlPlaneClientDiagnosticsTests
{
    [TestMethod]
    public async Task GetTelemetryAndBootstrapAsync_WriteRawJsonResponses_ToInboxFolder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var telemetryJson = """
                {"migration":{"workItems":{"completed":42,"revisionsProcessed":314}},"scope":{"workItemsTotal":100}}
                """;
            var bootstrapJson = """
                {"snapshot":null,"metrics":{"migration":{"workItems":{"completed":42}}},"lastEventSequence":7,"tasks":{"tasks":[],"phases":[{"name":"Export","order":0,"taskIds":["export.workitems.org.project"]}],"pushedAt":"2026-05-08T13:27:04.2055597+00:00","forKind":0}}
                """;

            using var httpClient = new HttpClient(new DelegatingHandlerStub(request =>
            {
                if (request.RequestUri?.AbsolutePath.EndsWith("/telemetry", StringComparison.Ordinal) == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(telemetryJson, Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri?.AbsolutePath.EndsWith("/bootstrap", StringComparison.Ordinal) == true)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(bootstrapJson, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }))
            {
                BaseAddress = new Uri("http://localhost:5100")
            };

            var recorder = new ControlPlaneCommunicationRecorder(tempRoot);
            var client = new ControlPlaneClient(httpClient, NullLogger<ControlPlaneClient>.Instance, diagnosticsRecorder: recorder);

            _ = await client.GetTelemetryAsync(Guid.NewGuid(), CancellationToken.None);
            var bootstrap = await client.GetBootstrapAsync(Guid.NewGuid(), CancellationToken.None);

            Assert.IsNotNull(bootstrap?.Tasks);
            Assert.AreEqual(1, bootstrap!.Tasks!.Phases.Count);
            Assert.AreEqual("Export", bootstrap.Tasks.Phases[0].Name);
            CollectionAssert.AreEqual(
                new[] { "export.workitems.org.project" },
                (System.Collections.ICollection)bootstrap.Tasks.Phases[0].TaskIds.ToArray());

            var inboxPath = Path.Combine(tempRoot, "inbox");
            var files = Directory.GetFiles(inboxPath, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();

            Assert.AreEqual(2, files.Length);
            StringAssert.EndsWith(files[0], "-telemetry.json");
            StringAssert.EndsWith(files[1], "-bootstrap.json");
            Assert.AreEqual(NormalizeJson(telemetryJson), NormalizeJson(await File.ReadAllTextAsync(files[0])));
            Assert.AreEqual(NormalizeJson(bootstrapJson), NormalizeJson(await File.ReadAllTextAsync(files[1])));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task FollowLogsAsync_WritesEachProgressEvent_ToInboxFolderInArrivalOrder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var firstJson = "{" + "\"eventSequence\":1,\"module\":\"Job\",\"stage\":\"Job.Ready\",\"message\":\"ready\"}";
            var secondJson = "{" + "\"eventSequence\":2,\"module\":\"WorkItems\",\"stage\":\"Export\",\"message\":\"progress\"}";
            var ssePayload = string.Join('\n', new[]
            {
                $"data: {firstJson}",
                string.Empty,
                $"data: {secondJson}",
                string.Empty,
                "event: job-ended",
                string.Empty
            });

            using var httpClient = new HttpClient(new DelegatingHandlerStub(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            }))
            {
                BaseAddress = new Uri("http://localhost:5100")
            };

            var recorder = new ControlPlaneCommunicationRecorder(tempRoot);
            var client = new ControlPlaneClient(httpClient, NullLogger<ControlPlaneClient>.Instance, diagnosticsRecorder: recorder);

            var received = new List<ProgressEvent>();
            await foreach (var evt in client.FollowLogsAsync(Guid.NewGuid(), CancellationToken.None))
                received.Add(evt);

            var inboxPath = Path.Combine(tempRoot, "inbox");
            var files = Directory.GetFiles(inboxPath, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(2, files.Length);
            StringAssert.EndsWith(files[0], "-progress-job-job-ready.json");
            StringAssert.EndsWith(files[1], "-progress-workitems-export.json");
            Assert.AreEqual(NormalizeJson(firstJson), NormalizeJson(await File.ReadAllTextAsync(files[0])));
            Assert.AreEqual(NormalizeJson(secondJson), NormalizeJson(await File.ReadAllTextAsync(files[1])));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task StreamDiagnosticsAsync_DoesNotWriteHttpNoise_ToInboxFolder()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var httpNoiseJson = """
                {"timestamp":"2026-05-08T14:26:17.0000000+00:00","level":"Information","category":"System.Net.Http.HttpClient.ControlPlaneClient","message":"Sending HTTP request"}
                """;
            var platformJson = """
                {"timestamp":"2026-05-08T14:26:18.0000000+00:00","level":"Information","category":"DevOpsMigrationPlatform.ControlPlane.JobProgressController","message":"Forwarded progress"}
                """;
            var ssePayload = string.Join('\n', new[]
            {
                $"data: {httpNoiseJson}",
                string.Empty,
                $"data: {platformJson}",
                string.Empty,
                "event: job-ended",
                string.Empty
            });

            using var httpClient = new HttpClient(new DelegatingHandlerStub(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ssePayload, Encoding.UTF8, "text/event-stream")
            }))
            {
                BaseAddress = new Uri("http://localhost:5100")
            };

            var recorder = new ControlPlaneCommunicationRecorder(tempRoot);
            var client = new ControlPlaneClient(httpClient, NullLogger<ControlPlaneClient>.Instance, diagnosticsRecorder: recorder);

            var received = new List<DiagnosticLogRecord>();
            await foreach (var record in client.StreamDiagnosticsAsync(Guid.NewGuid(), null, CancellationToken.None))
                received.Add(record);

            var inboxPath = Path.Combine(tempRoot, "inbox");
            var files = Directory.GetFiles(inboxPath, "*.json").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(1, files.Length, "Only platform diagnostics should be persisted to inbox.");
            StringAssert.EndsWith(files[0], "-diagnostics.json");
            Assert.AreEqual(NormalizeJson(platformJson), NormalizeJson(await File.ReadAllTextAsync(files[0])));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ControlPlaneClientDiagnosticsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NormalizeJson(string json)
        => json.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();

    private sealed class DelegatingHandlerStub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public DelegatingHandlerStub(Func<HttpRequestMessage, HttpResponseMessage> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}