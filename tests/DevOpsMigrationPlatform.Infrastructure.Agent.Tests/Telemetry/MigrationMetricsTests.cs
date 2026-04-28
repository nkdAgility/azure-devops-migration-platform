#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class MigrationMetricsTests
{
    private MeterListener _listener = null!;
    private readonly List<(string Name, object Value, KeyValuePair<string, object?>[] Tags)> _recorded = new();

    [TestInitialize]
    public void Setup()
    {
        _listener = new MeterListener();
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Migration)
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

    private static TagList CreateTestTags() =>
        MigrationTagList.Create("test-job-1", "export", "workitems");

    // --- Execution ---

    [TestMethod]
    public void RecordWorkItemAttempted_EmitsCorrectInstrumentAndTags()
    {
        using var sut = new MigrationMetrics();
        var tags = CreateTestTags();

        sut.RecordWorkItemAttempted(tags);

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.WorkItemsAttempted);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
        AssertHasTag(entry.Tags, "operation", "export");
        AssertHasTag(entry.Tags, "module", "workitems");
    }

    [TestMethod]
    public void RecordWorkItemCompleted_EmitsCorrectInstrument()
    {
        using var sut = new MigrationMetrics();
        sut.RecordWorkItemCompleted(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.WorkItemsCompleted);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordWorkItemFailed_EmitsCorrectInstrument()
    {
        using var sut = new MigrationMetrics();
        sut.RecordWorkItemFailed(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.WorkItemsFailed);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordWorkItemRetried_EmitsCorrectInstrument()
    {
        using var sut = new MigrationMetrics();
        sut.RecordWorkItemRetried(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.WorkItemsRetried);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordWorkItemDuration_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordWorkItemDuration(42.5, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.WorkItemDurationMs);
        Assert.AreEqual(42.5, entry.Value);
    }

    // --- Payload ---

    [TestMethod]
    public void RecordFieldCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordFieldCount(15, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.FieldCount);
        Assert.AreEqual(15, entry.Value);
    }

    [TestMethod]
    public void RecordAttachmentCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordAttachmentCount(3, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.AttachmentCount);
        Assert.AreEqual(3, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordLinkCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordLinkCount(8, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.LinkCount);
        Assert.AreEqual(8, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordRevisionCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionCount(12, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionCount);
        Assert.AreEqual(12, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordPayloadBytes_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordPayloadBytes(65536, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.PayloadBytes);
        Assert.AreEqual(65536L, entry.Value);
    }

    // --- Correctness ---

    [TestMethod]
    public void RecordRevisionSourceCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionSourceCount(10, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionSourceCount);
        Assert.AreEqual(10, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordRevisionTargetCount_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionTargetCount(10, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionTargetCount);
        Assert.AreEqual(10, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordRevisionDelta_EmitsHistogramValue()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionDelta(-2, CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionDelta);
        Assert.AreEqual(-2, entry.Value);
    }

    [TestMethod]
    public void RecordRevisionsMissing_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionsMissing(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionsMissing);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordRevisionOrderError_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordRevisionOrderError(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.RevisionOrderErrors);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordBrokenLink_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordBrokenLink(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.BrokenLinks);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordMissingWorkItem_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordMissingWorkItem(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.MissingWorkItems);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    // --- In-Flight ---

    [TestMethod]
    public void IncrementDecrementInFlight_EmitsUpDownCounter()
    {
        using var sut = new MigrationMetrics();
        var tags = CreateTestTags();

        sut.IncrementInFlight(tags);
        sut.IncrementInFlight(tags);
        sut.DecrementInFlight(tags);

        var entries = _recorded.Where(r => r.Name == WellKnownMetricNames.WorkItemsInFlight).ToList();
        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual(1, entries[0].Value);   // increment
        Assert.AreEqual(1, entries[1].Value);   // increment
        Assert.AreEqual(-1, entries[2].Value);  // decrement
    }

    // --- Idempotency ---

    [TestMethod]
    public void RecordDuplicated_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordDuplicated(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.Duplicated);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordChangedOnRerun_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordChangedOnRerun(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.ChangedOnRerun);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordReprocessedAfterResume_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordReprocessedAfterResume(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.ReprocessedAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordDuplicatedAfterResume_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordDuplicatedAfterResume(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.DuplicatedAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    [TestMethod]
    public void RecordMissingAfterResume_EmitsCounter()
    {
        using var sut = new MigrationMetrics();
        sut.RecordMissingAfterResume(CreateTestTags());

        var entry = _recorded.Single(r => r.Name == WellKnownMetricNames.MissingAfterResume);
        Assert.AreEqual(1L, entry.Value);
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
    }

    // --- Tag verification ---

    [TestMethod]
    public void AllMethods_CarryMandatoryTags()
    {
        using var sut = new MigrationMetrics();
        var tags = CreateTestTags();

        sut.RecordWorkItemAttempted(tags);

        var entry = _recorded.First();
        AssertHasTag(entry.Tags, "job.id", "test-job-1");
        AssertHasTag(entry.Tags, "operation", "export");
        AssertHasTag(entry.Tags, "module", "workitems");
    }

    private static void AssertHasTag(KeyValuePair<string, object?>[] tags, string key, string expectedValue)
    {
        var tag = tags.FirstOrDefault(t => t.Key == key);
        Assert.IsNotNull(tag.Key, $"Tag '{key}' not found");
        Assert.AreEqual(expectedValue, tag.Value?.ToString(), $"Tag '{key}' has wrong value");
    }
}
#endif
