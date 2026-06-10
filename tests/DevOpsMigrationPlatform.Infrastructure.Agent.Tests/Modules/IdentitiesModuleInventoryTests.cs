// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class IdentitiesModuleInventoryTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CaptureAsync_EmitsInventoryIdentitiesActivityWithJobAndModuleTags()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WellKnownActivitySourceNames.Discovery,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var module = CreateModule();
        await module.CaptureAsync(CreateContext(), CancellationToken.None);

        var activity = activities.Single(a => a.OperationName == "inventory.identities");
        Assert.AreEqual("job-1", activity.Tags.First(t => t.Key == "job.id").Value);
        Assert.AreEqual("Identities", activity.Tags.First(t => t.Key == "module").Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CaptureAsync_RecordsIdentityInventoryMetrics()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);
        metrics.Setup(m => m.RecordInventoryIdentities(
                2,
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", "job-1") && HasTag(t, "module", "Identities"))))
            .Verifiable();

        var module = CreateModule(metrics: metrics.Object);
        await module.CaptureAsync(CreateContext(), CancellationToken.None);

        metrics.Verify();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CaptureAsync_EmitsStartAndCompletionProgressWithMetrics()
    {
        var sink = new Mock<IProgressSink>(MockBehavior.Loose);
        var events = new List<ProgressEvent>();
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>())).Callback<ProgressEvent>(events.Add);

        var module = CreateModule();
        await module.CaptureAsync(CreateContext(progressSink: sink.Object), CancellationToken.None);

        Assert.IsTrue(events.Any(e => e.Stage == "Inventorying"));
        Assert.IsTrue(events.Any(e => e.Stage == "Inventoried" && e.Metrics is not null));
    }

    private static IdentitiesModule CreateModule(
        IPlatformMetrics? metrics = null)
    {
        var sourceEndpoint = new Mock<ISourceEndpointInfo>();
        sourceEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        sourceEndpoint.SetupGet(s => s.Url).Returns("https://source.example");
        sourceEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        return new IdentitiesModule(
            NullLogger<IdentitiesModule>.Instance,
            Options.Create(new IdentitiesModuleOptions { Enabled = true }),
            sourceEndpoint.Object,
            new IdentitiesOrchestrator(NullLogger<IdentitiesOrchestrator>.Instance),
            metrics,
            new StubIdentitySource());
    }

    private static InventoryContext CreateContext(IProgressSink? progressSink = null)
    {
        return new InventoryContext
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
            Package = PackageTestFactory.CreateLooseMock().Object,
            ProgressSink = progressSink,
            Project = "ProjectA"
        };
    }

    private static bool HasTag(MetricsTagList tags, string key, string value)
        => tags.Any(t => t.Key == key && string.Equals(t.Value?.ToString(), value, System.StringComparison.Ordinal));

    private sealed class StubIdentitySource : IIdentitySource
    {
        public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(string projectName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new IdentityDescriptor("d1", "User One", "user1@example.com", "User", "Simulated", true);
            await Task.Yield();
            yield return new IdentityDescriptor("d2", "User Two", "user2@example.com", "User", "Simulated", true);
        }
    }
}
