// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
public class PlatformMetricsTests
{
    private MeterListener _listener = null!;
    private readonly List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> _recorded = new();

    [TestInitialize]
    public void Setup()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Agent)
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

    private static MetricsTagList CreateExecutionTags() =>
        MetricsTagList.Create("test-job-1", "export", "workitems");

    // --- Idempotency Instrument Registration ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void IdempotencyCounters_AreRegisteredAtStartup()
    {
        var publishedNames = new List<string>();
        using var registrationListener = new MeterListener();
        registrationListener.InstrumentPublished = (instrument, _) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Agent)
                publishedNames.Add(instrument.Name);
        };
        registrationListener.Start();

        using var sut = new PlatformMetrics();

        Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.Duplicated),
            $"Expected {WellKnownAgentMetricNames.Duplicated} to be registered");
        Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.ChangedOnRerun),
            $"Expected {WellKnownAgentMetricNames.ChangedOnRerun} to be registered");
        Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.ReprocessedAfterResume),
            $"Expected {WellKnownAgentMetricNames.ReprocessedAfterResume} to be registered");
        Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.DuplicatedAfterResume),
            $"Expected {WellKnownAgentMetricNames.DuplicatedAfterResume} to be registered");
        Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.MissingAfterResume),
            $"Expected {WellKnownAgentMetricNames.MissingAfterResume} to be registered");
    }

    // --- Organisation ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void OrganisationStarted_EmitsUpDownCounter()
    {
        using var sut = new PlatformMetrics();
        sut.OrganisationStarted(CreateOrgTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.OrganisationsQueued);
        Assert.AreEqual(1, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
        AssertHasTag(entry.Tags, "module", "Inventory");
        AssertHasTag(entry.Tags, "organisation.url", "https://dev.azure.com/testorg");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void OrganisationCompleted_DecrementsQueueAndIncrementsCompleted()
    {
        using var sut = new PlatformMetrics();
        sut.OrganisationCompleted(CreateOrgTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.OrganisationsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.OrganisationsCompleted));
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void OrganisationFailed_DecrementsQueueAndIncrementsFailed()
    {
        using var sut = new PlatformMetrics();
        sut.OrganisationFailed(CreateOrgTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.OrganisationsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.OrganisationsFailed));
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordOrganisationDuration_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordOrganisationDuration(1500.5, CreateOrgTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.OrganisationDurationMs);
        Assert.AreEqual(1500.5, entry.Value);
    }

    // --- Project ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void ProjectStarted_EmitsUpDownCounter()
    {
        using var sut = new PlatformMetrics();
        sut.ProjectStarted(CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.ProjectsQueued);
        Assert.AreEqual(1, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void ProjectCompleted_DecrementsQueueAndIncrementsCompleted()
    {
        using var sut = new PlatformMetrics();
        sut.ProjectCompleted(CreateProjectTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.ProjectsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.ProjectsCompleted));
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void ProjectFailed_DecrementsQueueAndIncrementsFailed()
    {
        using var sut = new PlatformMetrics();
        sut.ProjectFailed(CreateProjectTags());

        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.ProjectsQueued && (int)r.Value == -1));
        Assert.IsTrue(_recorded.Any(r =>
            r.Name == WellKnownAgentMetricNames.ProjectsFailed));
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordProjectDuration_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordProjectDuration(250.0, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.ProjectDurationMs);
        Assert.AreEqual(250.0, entry.Value);
    }

    // --- Inventory ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemsCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemsCounted(350, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.InventoryWorkItems);
        Assert.AreEqual(350L, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionsCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionsCounted(1200, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.InventoryRevisions);
        Assert.AreEqual(1200L, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordReposCounted_EmitsCounterWithCorrectValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordReposCounted(5, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.InventoryRepos);
        Assert.AreEqual(5L, entry.Value);
    }

    // --- Dependencies ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordLinksFound_EmitsCounterWithCorrectValue()
    {
        using var sut = new PlatformMetrics();
        var tags = new MetricsTagList
        {
            { "job.id", "test-job-1" },
            { "module", "Dependencies" },
            { "organisation.url", "https://dev.azure.com/testorg" },
            { "link.scope", "CrossOrganisation" }
        };
        sut.RecordLinksFound(7, tags);

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.DependencyLinks);
        Assert.AreEqual(7L, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemsAnalysed_EmitsCounterWithCorrectValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemsAnalysed(500, CreateProjectTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.DependencyWorkItemsAnalysed);
        Assert.AreEqual(500L, entry.Value);
    }

    // --- Operational ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordCheckpointSaved_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        var tags = new MetricsTagList { { "job.id", "test-job-1" }, { "module", "Inventory" } };
        sut.RecordCheckpointSaved(tags);

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.CheckpointsSaved);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordJobDuration_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        var tags = new MetricsTagList
        {
            { "job.id", "test-job-1" },
            { "discovery.type", "Inventory" }
        };
        sut.RecordJobDuration(60000.0, tags);

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.JobDurationMs);
        Assert.AreEqual(60000.0, entry.Value);
    }

    // --- Execution ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemAttempted_EmitsCorrectInstrumentAndTags()
    {
        using var sut = new PlatformMetrics();
        var tags = CreateExecutionTags();

        sut.RecordWorkItemAttempted(tags);

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.WorkItemsAttempted);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
        AssertHasTag(entry.Tags, "operation", "export");
        AssertHasTag(entry.Tags, "module", "workitems");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemCompleted_EmitsCorrectInstrument()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemCompleted(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.WorkItemsCompleted);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemFailed_EmitsCorrectInstrument()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemFailed(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.WorkItemsFailed);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemRetried_EmitsCorrectInstrument()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemRetried(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.WorkItemsRetried);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordWorkItemDuration_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordWorkItemDuration(42.5, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.WorkItemDurationMs);
        Assert.AreEqual(42.5, entry.Value);
    }

    // --- Payload ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordFieldCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordFieldCount(15, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.FieldCount);
        Assert.AreEqual(15, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordAttachmentCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordAttachmentCount(3, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.AttachmentCount);
        Assert.AreEqual(3, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordLinkCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordLinkCount(8, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.LinkCount);
        Assert.AreEqual(8, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionCount(12, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionCount);
        Assert.AreEqual(12, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordPayloadBytes_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordPayloadBytes(65536, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.PayloadBytes);
        Assert.AreEqual(65536L, entry.Value);
    }

    // --- Correctness ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionSourceCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionSourceCount(10, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionSourceCount);
        Assert.AreEqual(10, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionTargetCount_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionTargetCount(10, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionTargetCount);
        Assert.AreEqual(10, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionDelta_EmitsHistogramValue()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionDelta(-2, CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionDelta);
        Assert.AreEqual(-2, entry.Value);
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionsMissing_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionsMissing(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionsMissing);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordRevisionOrderError_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordRevisionOrderError(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.RevisionOrderErrors);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordBrokenLink_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordBrokenLink(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.BrokenLinks);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordMissingWorkItem_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordMissingWorkItem(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.MissingWorkItems);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    // --- In-Flight ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void IncrementDecrementInFlight_EmitsUpDownCounter()
    {
        using var sut = new PlatformMetrics();
        var tags = CreateExecutionTags();

        sut.IncrementInFlight(tags);
        sut.IncrementInFlight(tags);
        sut.DecrementInFlight(tags);

        var entries = _recorded.Where(r => r.Name == WellKnownAgentMetricNames.WorkItemsInFlight).ToList();
        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(1, entries[0].Value);   // increment
        Assert.AreEqual(1, entries[1].Value);   // increment
        Assert.AreEqual(-1, entries[2].Value);  // decrement
    }

    // --- Idempotency ---

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordDuplicated_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordDuplicated(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.Duplicated);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordChangedOnRerun_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordChangedOnRerun(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.ChangedOnRerun);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordReprocessedAfterResume_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordReprocessedAfterResume(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.ReprocessedAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordDuplicatedAfterResume_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordDuplicatedAfterResume(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.DuplicatedAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    [TestCategory("UnitTest")]
    public void RecordMissingAfterResume_EmitsCounter()
    {
        using var sut = new PlatformMetrics();
        sut.RecordMissingAfterResume(CreateExecutionTags());

        var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.MissingAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    private static void AssertHasTag(KeyValuePair<string, object?>[] tags, string key, string expectedValue)
    {
        var tag = tags.FirstOrDefault(t => t.Key == key);
        Assert.IsNotNull(tag.Key, $"Tag '{key}' not found");
        Assert.AreEqual(expectedValue, tag.Value?.ToString(), $"Tag '{key}' has wrong value");
    }
}
#endif
