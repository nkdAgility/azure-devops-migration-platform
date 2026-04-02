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
        var snapshot = new MetricSnapshot { WorkItemsExported = 7 };

        _sut.Update(snapshot);

        Assert.IsNotNull(_sut.Latest);
        Assert.AreEqual(7, _sut.Latest!.WorkItemsExported);
    }

    [TestMethod]
    public void Update_WhenCalledTwice_LatestReturnsLastSnapshot()
    {
        _sut.Update(new MetricSnapshot { WorkItemsExported = 1 });
        _sut.Update(new MetricSnapshot { WorkItemsExported = 2 });

        Assert.AreEqual(2, _sut.Latest!.WorkItemsExported);
    }

    [TestMethod]
    public void Update_PreservesAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new MetricSnapshot
        {
            Timestamp              = now,
            WorkItemsExported      = 10,
            RevisionsExported      = 50,
            RevisionErrors         = 1,
            LinksExported          = 20,
            LinkErrors             = 0,
            AttachmentsAttempted   = 8,
            AttachmentsSucceeded   = 7,
            AttachmentsFailed      = 1,
            WorkItemDurationMeanMs = 123.4,
            RevisionDurationMeanMs = 45.6,
            TotalExportDurationMs  = 78900.0
        };

        _sut.Update(snapshot);
        var result = _sut.Latest!;

        Assert.AreEqual(now,    result.Timestamp);
        Assert.AreEqual(10,     result.WorkItemsExported);
        Assert.AreEqual(50,     result.RevisionsExported);
        Assert.AreEqual(1,      result.RevisionErrors);
        Assert.AreEqual(20,     result.LinksExported);
        Assert.AreEqual(0,      result.LinkErrors);
        Assert.AreEqual(8,      result.AttachmentsAttempted);
        Assert.AreEqual(7,      result.AttachmentsSucceeded);
        Assert.AreEqual(1,      result.AttachmentsFailed);
        Assert.AreEqual(123.4,  result.WorkItemDurationMeanMs);
        Assert.AreEqual(45.6,   result.RevisionDurationMeanMs);
        Assert.AreEqual(78900.0,result.TotalExportDurationMs);
    }

    [TestMethod]
    public void Update_WithNullFields_StillStoresSnapshot()
    {
        var snapshot = new MetricSnapshot
        {
            WorkItemsExported      = 3,
            WorkItemDurationMeanMs = null,
            RevisionDurationMeanMs = null,
            TotalExportDurationMs  = null
        };

        _sut.Update(snapshot);

        Assert.IsNotNull(_sut.Latest);
        Assert.IsNull(_sut.Latest!.WorkItemDurationMeanMs);
        Assert.IsNull(_sut.Latest!.RevisionDurationMeanMs);
        Assert.IsNull(_sut.Latest!.TotalExportDurationMs);
    }
}
