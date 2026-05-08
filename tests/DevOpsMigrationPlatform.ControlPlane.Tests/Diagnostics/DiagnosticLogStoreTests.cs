// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.ControlPlane.Jobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.ControlPlane.Tests.Diagnostics;

[TestClass]
public sealed class DiagnosticLogStoreTests
{
    private static readonly Guid JobId = new("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord()
    {
        var store = CreateStore(capacity: 2, minimumLevel: "Information");

        store.Add(JobId, new[]
        {
            MakeRecord("Information", "first"),
            MakeRecord("Warning", "second"),
            MakeRecord("Error", "third"),
        });

        var snapshot = store.GetSnapshot(JobId);

        Assert.AreEqual(2, snapshot.Count);
        CollectionAssert.AreEqual(
            new[] { "second", "third" },
            snapshot.Select(record => record.Message).ToArray());
    }

    [TestMethod]
    public void Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering()
    {
        var store = CreateStore(capacity: 5, minimumLevel: "Warning");

        store.Add(JobId, new[]
        {
            MakeRecord("Information", "ignored"),
            MakeRecord("Warning", "retained warning"),
            MakeRecord("Error", "retained error"),
        });

        var snapshot = store.GetSnapshot(JobId);

        Assert.AreEqual(2, snapshot.Count);
        CollectionAssert.AreEqual(
            new[] { "retained warning", "retained error" },
            snapshot.Select(record => record.Message).ToArray());
    }

    [TestMethod]
    public void GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel()
    {
        var store = CreateStore(capacity: 5, minimumLevel: "Information");

        store.Add(JobId, new[]
        {
            MakeRecord("Information", "info"),
            MakeRecord("Warning", "warning"),
            MakeRecord("Error", "error"),
        });

        var snapshot = store.GetSnapshot(JobId, LogLevel.Warning);

        Assert.AreEqual(2, snapshot.Count);
        CollectionAssert.AreEqual(
            new[] { "warning", "error" },
            snapshot.Select(record => record.Message).ToArray());
    }

    [TestMethod]
    public void Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot()
    {
        var store = CreateStore(capacity: 5, minimumLevel: "Information");
        var (reader, writer) = store.Subscribe(JobId);
        var record = MakeRecord("Warning", "streamed warning");

        store.Add(JobId, new[] { record });

        Assert.IsTrue(reader.TryRead(out var streamed), "Expected the live subscriber to receive the appended record.");
        Assert.AreEqual("streamed warning", streamed.Message);

        store.Unsubscribe(JobId, writer);
    }

    [TestMethod]
    public void Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately()
    {
        var store = CreateStore(capacity: 5, minimumLevel: "Information");

        store.CompleteJob(JobId, failed: true);
        var (reader, _) = store.Subscribe(JobId);

        Assert.IsTrue(reader.Completion.IsCompleted);
        Assert.IsTrue(store.IsCompleted(JobId));
        Assert.IsTrue(store.WasFailed(JobId));
    }

    private static DiagnosticLogStore CreateStore(int capacity, string minimumLevel) =>
        new(Options.Create(new DiagnosticLogStoreOptions
        {
            Capacity = capacity,
            MinimumLevel = minimumLevel,
        }));

    private static DiagnosticLogRecord MakeRecord(string level, string message) => new()
    {
        Level = level,
        Category = "Test.Category",
        Message = message,
        DataClassification = "System",
    };
}
