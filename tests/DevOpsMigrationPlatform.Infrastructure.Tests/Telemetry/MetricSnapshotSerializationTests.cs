// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void RoundTrip_AllPropertiesPopulated_PreservesValues()
    {
        var original = new JobMetrics
        {
            Timestamp = new DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero),
            Scope = new JobScopeCounters
            {
                OrganisationsTotal = 2,
                OrganisationsCompleted = 1,
                OrganisationsFailed = 0,
                ProjectsTotal = 5,
                ProjectsCompleted = 3,
                ProjectsFailed = 1,
                WorkItemsTotal = 100
            },
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters
                {
                    Attempted = 100,
                    Completed = 95,
                    Failed = 3,
                    Skipped = 2,
                    RevisionsProcessed = 500,
                    Attachments = new AttachmentCounters
                    {
                        Processed = 50,
                        Failed = 2,
                        TotalBytes = 65536
                    }
                },
                Diagnostics = new MigrationDiagnostics
                {
                    WorkItemDurationMeanMs = 456.7,
                    FieldCountMean = 12.3,
                    AttachmentCountMean = 2.1,
                    LinkCountMean = 4.5,
                    RevisionCountMean = 8.0,
                    PayloadBytesMean = 65536.0,
                    RevisionsMissing = 0,
                    RevisionOrderErrors = 1,
                    BrokenLinks = 2,
                    MissingWorkItems = 0,
                    WorkItemsInFlight = 4,
                    QueueDepth = 50
                }
            }
        };

        var json = JsonSerializer.Serialize(original, CamelCase);
        var deserialized = JsonSerializer.Deserialize<JobMetrics>(json, CamelCase);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Timestamp, deserialized!.Timestamp);
        Assert.AreEqual(100, deserialized.Migration!.WorkItems.Attempted);
        Assert.AreEqual(95, deserialized.Migration.WorkItems.Completed);
        Assert.AreEqual(3, deserialized.Migration.WorkItems.Failed);
        Assert.AreEqual(2, deserialized.Migration.WorkItems.Skipped);
        Assert.AreEqual(500, deserialized.Migration.WorkItems.RevisionsProcessed);
        Assert.AreEqual(50, deserialized.Migration.WorkItems.Attachments!.Processed);
        Assert.AreEqual(2, deserialized.Migration.WorkItems.Attachments.Failed);
        Assert.AreEqual(65536, deserialized.Migration.WorkItems.Attachments.TotalBytes);
        Assert.AreEqual(456.7, deserialized.Migration.Diagnostics!.WorkItemDurationMeanMs);
        Assert.AreEqual(12.3, deserialized.Migration.Diagnostics.FieldCountMean);
        Assert.AreEqual(2.1, deserialized.Migration.Diagnostics.AttachmentCountMean);
        Assert.AreEqual(4.5, deserialized.Migration.Diagnostics.LinkCountMean);
        Assert.AreEqual(8.0, deserialized.Migration.Diagnostics.RevisionCountMean);
        Assert.AreEqual(65536.0, deserialized.Migration.Diagnostics.PayloadBytesMean);
        Assert.AreEqual(0, deserialized.Migration.Diagnostics.RevisionsMissing);
        Assert.AreEqual(1, deserialized.Migration.Diagnostics.RevisionOrderErrors);
        Assert.AreEqual(2, deserialized.Migration.Diagnostics.BrokenLinks);
        Assert.AreEqual(0, deserialized.Migration.Diagnostics.MissingWorkItems);
        Assert.AreEqual(4, deserialized.Migration.Diagnostics.WorkItemsInFlight);
        Assert.AreEqual(50, deserialized.Migration.Diagnostics.QueueDepth);
        Assert.AreEqual(2, deserialized.Scope.OrganisationsTotal);
        Assert.AreEqual(1, deserialized.Scope.OrganisationsCompleted);
        Assert.AreEqual(5, deserialized.Scope.ProjectsTotal);
        Assert.AreEqual(3, deserialized.Scope.ProjectsCompleted);
        Assert.AreEqual(1, deserialized.Scope.ProjectsFailed);
        Assert.AreEqual(100, deserialized.Scope.WorkItemsTotal);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void RoundTrip_NullOptionalProperties_SerializesCorrectly()
    {
        var original = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 5 },
                Diagnostics = new MigrationDiagnostics
                {
                    WorkItemDurationMeanMs = null,
                    FieldCountMean = null
                }
            }
        };

        var json = JsonSerializer.Serialize(original, CamelCase);
        var deserialized = JsonSerializer.Deserialize<JobMetrics>(json, CamelCase);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(5, deserialized!.Migration!.WorkItems.Attempted);
        Assert.IsNull(deserialized.Migration.Diagnostics!.WorkItemDurationMeanMs);
        Assert.IsNull(deserialized.Migration.Diagnostics.FieldCountMean);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Serialize_UsesCamelCasePropertyNames()
    {
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 1 }
            }
        };
        var json = JsonSerializer.Serialize(metrics, CamelCase);

        Assert.IsTrue(json.Contains("\"migration\""), "Expected camelCase property name for Migration");
        Assert.IsFalse(json.Contains("\"Migration\""), "PascalCase should not appear");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void RoundTrip_DiscoveryCounters_PreservesValues()
    {
        var original = new JobMetrics
        {
            Scope = new JobScopeCounters { WorkItemsTotal = 500 },
            Discovery = new DiscoveryCounters
            {
                Inventory = new InventoryCounters
                {
                    RevisionsTotal = 1000,
                    RepositoriesTotal = 10,
                    CheckpointsSaved = 5
                },
                Dependencies = new DependencyCounters
                {
                    WorkItemsAnalysed = 400,
                    ExternalLinksFound = 50,
                    CrossProjectLinks = 20,
                    CrossOrgLinks = 5,
                    CheckpointsSaved = 3
                }
            }
        };

        var json = JsonSerializer.Serialize(original, CamelCase);
        var deserialized = JsonSerializer.Deserialize<JobMetrics>(json, CamelCase);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(500, deserialized!.Scope.WorkItemsTotal);
        Assert.AreEqual(1000, deserialized.Discovery!.Inventory!.RevisionsTotal);
        Assert.AreEqual(10, deserialized.Discovery.Inventory.RepositoriesTotal);
        Assert.AreEqual(5, deserialized.Discovery.Inventory.CheckpointsSaved);
        Assert.AreEqual(400, deserialized.Discovery.Dependencies!.WorkItemsAnalysed);
        Assert.AreEqual(50, deserialized.Discovery.Dependencies.ExternalLinksFound);
        Assert.AreEqual(20, deserialized.Discovery.Dependencies.CrossProjectLinks);
        Assert.AreEqual(5, deserialized.Discovery.Dependencies.CrossOrgLinks);
        Assert.AreEqual(3, deserialized.Discovery.Dependencies.CheckpointsSaved);
    }
}
