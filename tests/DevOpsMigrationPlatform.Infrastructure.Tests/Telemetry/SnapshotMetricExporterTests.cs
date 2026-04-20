using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

// ─────────────────────────────────────────────────────────────────────────────
// InMemoryMetricSnapshotStore — pure unit tests (no OTel dependency)
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class InMemoryMetricSnapshotStoreTests
{
    private InMemoryMetricSnapshotStore _sut = null!;

    [TestInitialize]
    public void Setup() => _sut = new InMemoryMetricSnapshotStore();

    [TestMethod]
    public void Latest_BeforeAnyUpdate_ReturnsNull()
    {
        Assert.IsNull(_sut.Latest);
    }

    [TestMethod]
    public void Update_WhenCalledOnce_LatestReturnsSnapshot()
    {
        var snapshot = new MetricSnapshot { WorkItemsAttempted = 7 };

        _sut.Update(snapshot);

        Assert.IsNotNull(_sut.Latest);
        Assert.AreEqual(7, _sut.Latest!.WorkItemsAttempted);
    }

    [TestMethod]
    public void Update_WhenCalledTwice_LatestReturnsLastSnapshot()
    {
        _sut.Update(new MetricSnapshot { WorkItemsAttempted = 1 });
        _sut.Update(new MetricSnapshot { WorkItemsAttempted = 2 });

        Assert.AreEqual(2, _sut.Latest!.WorkItemsAttempted);
    }

    [TestMethod]
    public void Update_PreservesAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new MetricSnapshot
        {
            Timestamp = now,
            WorkItemsAttempted = 10,
            WorkItemsCompleted = 9,
            WorkItemsFailed = 1,
            WorkItemsRetried = 2,
            WorkItemDurationMeanMs = 123.4,
            FieldCountMean = 5.0,
            AttachmentCountMean = 2.5,
            LinkCountMean = 3.0,
            RevisionCountMean = 7.0,
            PayloadBytesMean = 4096.0,
            RevisionSourceCountMean = 7.0,
            RevisionTargetCountMean = 7.0,
            RevisionDeltaMean = 0.0,
            RevisionsMissing = 0,
            RevisionOrderErrors = 0,
            BrokenLinks = 1,
            MissingWorkItems = 0,
            WorkItemsInFlight = 3,
            QueueDepth = 47,
            Duplicated = 0,
            ChangedOnRerun = null,
            ReprocessedAfterResume = null,
            DuplicatedAfterResume = null,
            MissingAfterResume = null,
        };

        _sut.Update(snapshot);
        var result = _sut.Latest!;

        Assert.AreEqual(now, result.Timestamp);
        Assert.AreEqual(10, result.WorkItemsAttempted);
        Assert.AreEqual(9, result.WorkItemsCompleted);
        Assert.AreEqual(1, result.WorkItemsFailed);
        Assert.AreEqual(2, result.WorkItemsRetried);
        Assert.AreEqual(123.4, result.WorkItemDurationMeanMs);
        Assert.AreEqual(5.0, result.FieldCountMean);
        Assert.AreEqual(2.5, result.AttachmentCountMean);
        Assert.AreEqual(3.0, result.LinkCountMean);
        Assert.AreEqual(7.0, result.RevisionCountMean);
        Assert.AreEqual(4096.0, result.PayloadBytesMean);
        Assert.AreEqual(7.0, result.RevisionSourceCountMean);
        Assert.AreEqual(7.0, result.RevisionTargetCountMean);
        Assert.AreEqual(0.0, result.RevisionDeltaMean);
        Assert.AreEqual(0, result.RevisionsMissing);
        Assert.AreEqual(0, result.RevisionOrderErrors);
        Assert.AreEqual(1, result.BrokenLinks);
        Assert.AreEqual(0, result.MissingWorkItems);
        Assert.AreEqual(3, result.WorkItemsInFlight);
        Assert.AreEqual(47, result.QueueDepth);
        Assert.AreEqual(0L, result.Duplicated);
        Assert.IsNull(result.ChangedOnRerun);
        Assert.IsNull(result.ReprocessedAfterResume);
        Assert.IsNull(result.DuplicatedAfterResume);
        Assert.IsNull(result.MissingAfterResume);
    }

    [TestMethod]
    public void Update_WithNullFields_StillStoresSnapshot()
    {
        var snapshot = new MetricSnapshot
        {
            WorkItemsAttempted = 3,
            WorkItemDurationMeanMs = null,
            Duplicated = null,
            ChangedOnRerun = null,
        };

        _sut.Update(snapshot);

        Assert.IsNotNull(_sut.Latest);
        Assert.IsNull(_sut.Latest!.WorkItemDurationMeanMs);
        Assert.IsNull(_sut.Latest!.Duplicated);
        Assert.IsNull(_sut.Latest!.ChangedOnRerun);
    }
}
