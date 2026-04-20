using System;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

[TestClass]
public class MetricSnapshotSerializationTests
{
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    [TestMethod]
    public void RoundTrip_AllPropertiesPopulated_PreservesValues()
    {
        var original = new MetricSnapshot
        {
            Timestamp = new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero),
            WorkItemsAttempted = 100,
            WorkItemsCompleted = 95,
            WorkItemsFailed = 3,
            WorkItemsRetried = 2,
            WorkItemDurationMeanMs = 456.7,
            FieldCountMean = 12.3,
            AttachmentCountMean = 2.1,
            LinkCountMean = 4.5,
            RevisionCountMean = 8.0,
            PayloadBytesMean = 65536.0,
            RevisionSourceCountMean = 8.0,
            RevisionTargetCountMean = 8.0,
            RevisionDeltaMean = 0.0,
            RevisionsMissing = 0,
            RevisionOrderErrors = 1,
            BrokenLinks = 2,
            MissingWorkItems = 0,
            WorkItemsInFlight = 4,
            QueueDepth = 50,
            Duplicated = 1,
            ChangedOnRerun = 0,
            ReprocessedAfterResume = 3,
            DuplicatedAfterResume = 0,
            MissingAfterResume = 1,
        };

        var json = JsonSerializer.Serialize(original, CamelCase);
        var deserialized = JsonSerializer.Deserialize<MetricSnapshot>(json, CamelCase);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Timestamp, deserialized!.Timestamp);
        Assert.AreEqual(original.WorkItemsAttempted, deserialized.WorkItemsAttempted);
        Assert.AreEqual(original.WorkItemsCompleted, deserialized.WorkItemsCompleted);
        Assert.AreEqual(original.WorkItemsFailed, deserialized.WorkItemsFailed);
        Assert.AreEqual(original.WorkItemsRetried, deserialized.WorkItemsRetried);
        Assert.AreEqual(original.WorkItemDurationMeanMs, deserialized.WorkItemDurationMeanMs);
        Assert.AreEqual(original.FieldCountMean, deserialized.FieldCountMean);
        Assert.AreEqual(original.AttachmentCountMean, deserialized.AttachmentCountMean);
        Assert.AreEqual(original.LinkCountMean, deserialized.LinkCountMean);
        Assert.AreEqual(original.RevisionCountMean, deserialized.RevisionCountMean);
        Assert.AreEqual(original.PayloadBytesMean, deserialized.PayloadBytesMean);
        Assert.AreEqual(original.RevisionSourceCountMean, deserialized.RevisionSourceCountMean);
        Assert.AreEqual(original.RevisionTargetCountMean, deserialized.RevisionTargetCountMean);
        Assert.AreEqual(original.RevisionDeltaMean, deserialized.RevisionDeltaMean);
        Assert.AreEqual(original.RevisionsMissing, deserialized.RevisionsMissing);
        Assert.AreEqual(original.RevisionOrderErrors, deserialized.RevisionOrderErrors);
        Assert.AreEqual(original.BrokenLinks, deserialized.BrokenLinks);
        Assert.AreEqual(original.MissingWorkItems, deserialized.MissingWorkItems);
        Assert.AreEqual(original.WorkItemsInFlight, deserialized.WorkItemsInFlight);
        Assert.AreEqual(original.QueueDepth, deserialized.QueueDepth);
        Assert.AreEqual(original.Duplicated, deserialized.Duplicated);
        Assert.AreEqual(original.ChangedOnRerun, deserialized.ChangedOnRerun);
        Assert.AreEqual(original.ReprocessedAfterResume, deserialized.ReprocessedAfterResume);
        Assert.AreEqual(original.DuplicatedAfterResume, deserialized.DuplicatedAfterResume);
        Assert.AreEqual(original.MissingAfterResume, deserialized.MissingAfterResume);
    }

    [TestMethod]
    public void RoundTrip_NullDeferredProperties_SerializesCorrectly()
    {
        var original = new MetricSnapshot
        {
            WorkItemsAttempted = 5,
            Duplicated = null,
            ChangedOnRerun = null,
            ReprocessedAfterResume = null,
            DuplicatedAfterResume = null,
            MissingAfterResume = null,
        };

        var json = JsonSerializer.Serialize(original, CamelCase);
        var deserialized = JsonSerializer.Deserialize<MetricSnapshot>(json, CamelCase);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(5, deserialized!.WorkItemsAttempted);
        Assert.IsNull(deserialized.Duplicated);
        Assert.IsNull(deserialized.ChangedOnRerun);
        Assert.IsNull(deserialized.ReprocessedAfterResume);
        Assert.IsNull(deserialized.DuplicatedAfterResume);
        Assert.IsNull(deserialized.MissingAfterResume);
    }

    [TestMethod]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var snapshot = new MetricSnapshot { WorkItemsAttempted = 1 };
        var json = JsonSerializer.Serialize(snapshot, CamelCase);

        Assert.IsTrue(json.Contains("\"workItemsAttempted\""), "Expected camelCase property name");
        Assert.IsFalse(json.Contains("\"WorkItemsAttempted\""), "PascalCase should not appear");
    }
}
