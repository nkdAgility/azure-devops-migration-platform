#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class DiscoveryMetricsTests
{
    private MeterListener _listener = null!;
    private readonly List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> _recorded = new();

    [TestInitialize]
    public void Setup()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Discovery)
                listener.EnableMeasurementEvents(instrument);
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));

        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));

        _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) =>
            _recorded.Add((instrument.Name, value, tags.ToArray())));

        _listener.Start();
    }

    [TestCleanup]
    public void Cleanup() => _listener.Dispose();

    private static MetricsTagList CreateOrgTags() =>
        new()
        {
            { "job.id", "test-job-1" },
            { "module", "Inventory" },
            { "organisation.url", "https://dev.azure.com/testorg" }
        };

    private static MetricsTagList CreateProjectTags() =>
        new()
        {
            { "job.id", "test-job-1" },
            { "module", "Inventory" },
            { "organisation.url", "https://dev.azure.com/testorg" },
            { "project.name", "TestProject" }
        };

    // --- Organisation ---

    [TestMethod]
    public void OrganisationStarted_EmitsUpDownCounter()
    {
        using var sut = new DiscoveryMetrics();
        sut.OrganisationStarted(CreateOrgTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.OrganisationsQueued);
        Assert.AreEqual(1, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
        AssertHasTag(entry.Tags, "module", "Inventory");
        AssertHasTag(entry.Tags, "organisation.url", "https://dev.azure.com/testorg");
    }

    [TestMethod]
    public void OrganisationCompleted_DecrementsQueueAndIncrementsCompleted()
    {
        using var sut = new DiscoveryMetrics();
        sut.OrganisationCompleted(CreateOrgTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.OrganisationsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.OrganisationsCompleted));
    }

    [TestMethod]
    public void OrganisationFailed_DecrementsQueueAndIncrementsFailed()
    {
        using var sut = new DiscoveryMetrics();
        sut.OrganisationFailed(CreateOrgTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.OrganisationsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.OrganisationsFailed));
    }

    [TestMethod]
    public void RecordOrganisationDuration_EmitsHistogramValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordOrganisationDuration(1500.5, CreateOrgTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.OrganisationDurationMs);
        Assert.AreEqual(1500.5, entry.Value);
    }

    // --- Project ---

    [TestMethod]
    public void ProjectStarted_EmitsUpDownCounter()
    {
        using var sut = new DiscoveryMetrics();
        sut.ProjectStarted(CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.ProjectsQueued);
        Assert.AreEqual(1, entry.Value);
    }

    [TestMethod]
    public void ProjectCompleted_DecrementsQueueAndIncrementsCompleted()
    {
        using var sut = new DiscoveryMetrics();
        sut.ProjectCompleted(CreateProjectTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.ProjectsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.ProjectsCompleted));
    }

    [TestMethod]
    public void ProjectFailed_DecrementsQueueAndIncrementsFailed()
    {
        using var sut = new DiscoveryMetrics();
        sut.ProjectFailed(CreateProjectTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.ProjectsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownDiscoveryMetricNames.ProjectsFailed));
    }

    [TestMethod]
    public void RecordProjectDuration_EmitsHistogramValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordProjectDuration(250.0, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.ProjectDurationMs);
        Assert.AreEqual(250.0, entry.Value);
    }

    // --- Inventory ---

    [TestMethod]
    public void RecordWorkItemsCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordWorkItemsCounted(350, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.InventoryWorkItems);
        Assert.AreEqual(350L, entry.Value);
    }

    [TestMethod]
    public void RecordRevisionsCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordRevisionsCounted(1200, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.InventoryRevisions);
        Assert.AreEqual(1200L, entry.Value);
    }

    [TestMethod]
    public void RecordReposCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordReposCounted(5, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.InventoryRepos);
        Assert.AreEqual(5L, entry.Value);
    }

    // --- Dependencies ---

    [TestMethod]
    public void RecordLinksFound_EmitsCounterWithCorrectValue()
    {
        using var sut = new DiscoveryMetrics();
        var tags = new MetricsTagList
        {
            { "job.id", "test-job-1" },
            { "module", "Dependencies" },
            { "organisation.url", "https://dev.azure.com/testorg" },
            { "link.scope", "CrossOrganisation" }
        };
        sut.RecordLinksFound(7, tags);

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.DependencyLinks);
        Assert.AreEqual(7L, entry.Value);
    }

    [TestMethod]
    public void RecordWorkItemsAnalysed_EmitsCounterWithCorrectValue()
    {
        using var sut = new DiscoveryMetrics();
        sut.RecordWorkItemsAnalysed(500, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.DependencyWorkItemsAnalysed);
        Assert.AreEqual(500L, entry.Value);
    }

    // --- Operational ---

    [TestMethod]
    public void RecordCheckpointSaved_EmitsCounter()
    {
        using var sut = new DiscoveryMetrics();
        var tags = new MetricsTagList { { "job.id", "test-job-1" }, { "module", "Inventory" } };
        sut.RecordCheckpointSaved(tags);

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.CheckpointsSaved);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordJobDuration_EmitsHistogramValue()
    {
        using var sut = new DiscoveryMetrics();
        var tags = new MetricsTagList
        {
            { "job.id", "test-job-1" },
            { "discovery.type", "Inventory" }
        };
        sut.RecordJobDuration(60000.0, tags);

        var entry = _recorded.Single(r => r.Name == WellKnownDiscoveryMetricNames.JobDurationMs);
        Assert.AreEqual(60000.0, entry.Value);
    }

    private static void AssertHasTag(KeyValuePair<string, object?>[] tags, string key, string expectedValue)
    {
        var tag = tags.FirstOrDefault(t => t.Key == key);
        Assert.IsNotNull(tag.Key, $"Tag '{key}' not found");
        Assert.AreEqual(expectedValue, tag.Value?.ToString(), $"Tag '{key}' has wrong value");
    }
}
#endif
