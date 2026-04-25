using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.ControlPlane.Tests.Metrics;

// ─────────────────────────────────────────────────────────────────────────────
// InMemoryJobMetricsStore — pure unit tests (no OTel dependency)
// ─────────────────────────────────────────────────────────────────────────────

[TestClass]
public class InMemoryJobMetricsStoreTests
{
    private InMemoryJobMetricsStore _sut = null!;

    [TestInitialize]
    public void Setup() => _sut = new InMemoryJobMetricsStore();

    [TestMethod]
    public void Latest_BeforeAnyUpdate_ReturnsNull()
    {
        Assert.IsNull(_sut.Latest);
    }

    [TestMethod]
    public void Update_WhenCalledOnce_LatestReturnsSnapshot()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 7 }
            }
        };

        _sut.Update(metrics);

        Assert.IsNotNull(_sut.Latest);
        Assert.AreEqual(7, _sut.Latest!.Migration!.WorkItems.Attempted);
    }

    [TestMethod]
    public void Update_WhenCalledTwice_LatestReturnsLastSnapshot()
    {
        _sut.Update(new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 1 }
            }
        });
        _sut.Update(new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 2 }
            }
        });

        Assert.AreEqual(2, _sut.Latest!.Migration!.WorkItems.Attempted);
    }

    [TestMethod]
    public void Update_PreservesAllFields()
    {
        var now = DateTimeOffset.UtcNow;
        var metrics = new JobMetrics
        {
            Timestamp = now,
            Scope = new JobScopeCounters
            {
                WorkItemsTotal = 100,
                ProjectsTotal = 2
            },
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters
                {
                    Attempted = 10,
                    Completed = 9,
                    Failed = 1,
                    Skipped = 0,
                    RevisionsProcessed = 42,
                    Attachments = new AttachmentCounters
                    {
                        Processed = 5,
                        Failed = 1,
                        TotalBytes = 4096
                    }
                },
                Diagnostics = new MigrationDiagnostics
                {
                    WorkItemDurationMeanMs = 123.4,
                    FieldCountMean = 5.0,
                    AttachmentCountMean = 2.5,
                    LinkCountMean = 3.0,
                    RevisionCountMean = 7.0,
                    PayloadBytesMean = 4096.0,
                    RevisionsMissing = 0,
                    RevisionOrderErrors = 0,
                    BrokenLinks = 1,
                    MissingWorkItems = 0,
                    WorkItemsInFlight = 3,
                    QueueDepth = 47
                }
            }
        };

        _sut.Update(metrics);
        var result = _sut.Latest!;

        Assert.AreEqual(now, result.Timestamp);
        Assert.AreEqual(10, result.Migration!.WorkItems.Attempted);
        Assert.AreEqual(9, result.Migration.WorkItems.Completed);
        Assert.AreEqual(1, result.Migration.WorkItems.Failed);
        Assert.AreEqual(42, result.Migration.WorkItems.RevisionsProcessed);
        Assert.AreEqual(5, result.Migration.WorkItems.Attachments!.Processed);
        Assert.AreEqual(1, result.Migration.WorkItems.Attachments.Failed);
        Assert.AreEqual(4096, result.Migration.WorkItems.Attachments.TotalBytes);
        Assert.AreEqual(123.4, result.Migration.Diagnostics!.WorkItemDurationMeanMs);
        Assert.AreEqual(5.0, result.Migration.Diagnostics.FieldCountMean);
        Assert.AreEqual(2.5, result.Migration.Diagnostics.AttachmentCountMean);
        Assert.AreEqual(3.0, result.Migration.Diagnostics.LinkCountMean);
        Assert.AreEqual(7.0, result.Migration.Diagnostics.RevisionCountMean);
        Assert.AreEqual(4096.0, result.Migration.Diagnostics.PayloadBytesMean);
        Assert.AreEqual(0, result.Migration.Diagnostics.RevisionsMissing);
        Assert.AreEqual(0, result.Migration.Diagnostics.RevisionOrderErrors);
        Assert.AreEqual(1, result.Migration.Diagnostics.BrokenLinks);
        Assert.AreEqual(0, result.Migration.Diagnostics.MissingWorkItems);
        Assert.AreEqual(3, result.Migration.Diagnostics.WorkItemsInFlight);
        Assert.AreEqual(47, result.Migration.Diagnostics.QueueDepth);
        Assert.AreEqual(100, result.Scope.WorkItemsTotal);
        Assert.AreEqual(2, result.Scope.ProjectsTotal);
    }

    [TestMethod]
    public void Update_WithNullFields_StillStoresSnapshot()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 3 },
                Diagnostics = new MigrationDiagnostics
                {
                    WorkItemDurationMeanMs = null
                }
            }
        };

        _sut.Update(metrics);

        Assert.IsNotNull(_sut.Latest);
        Assert.IsNull(_sut.Latest!.Migration!.Diagnostics!.WorkItemDurationMeanMs);
    }
}
