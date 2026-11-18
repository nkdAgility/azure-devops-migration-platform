// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Net;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// #171 live E2E: after a migration the CLI printed "Import complete — 0 work items imported"
/// although every work item was imported. The count came from the last progress event, which
/// carries no metrics; the job's telemetry holds the real count.
/// </summary>
[TestClass]
public sealed class ImportSummaryCountTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task GetTelemetryAsync_ReturnsTheJobsMetrics()
    {
        var jobId = Guid.NewGuid();
        string? path = null;
        var http = new HttpClient(new Stub(request =>
        {
            path = request.RequestUri!.AbsolutePath;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"migration\":{\"workItems\":{\"completed\":3}}}", Encoding.UTF8, "application/json")
            };
        }))
        { BaseAddress = new Uri("http://localhost:5100") };

        var metrics = await new ControlPlaneClient(http, NullLogger<ControlPlaneClient>.Instance)
            .GetTelemetryAsync(jobId, CancellationToken.None);

        Assert.AreEqual($"/jobs/{jobId}/telemetry", path);
        Assert.AreEqual(3, metrics?.Migration?.WorkItems.Completed);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ImportedCount_ProgressEventHasNoMetrics_UsesTheJobsTelemetry()
    {
        var telemetry = new JobMetrics { Migration = new MigrationCounters { WorkItems = new WorkItemCounters { Completed = 3 } } };

        Assert.AreEqual(3, QueueCommand.ImportedCount(telemetry, new ProgressEvent()));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ImportedCount_NoTelemetry_FallsBackToTheLastEvent()
    {
        var evt = new ProgressEvent { Metrics = new JobMetrics { Migration = new MigrationCounters { WorkItems = new WorkItemCounters { Completed = 2 } } } };

        Assert.AreEqual(2, QueueCommand.ImportedCount(null, evt));
    }

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
