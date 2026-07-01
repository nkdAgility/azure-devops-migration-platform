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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void Add_WhenSafetyCapReached_RetainsExistingAndDiscardsFurtherRecords()
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
            new[] { "first", "second" },
            snapshot.Select(record => record.Message).ToArray());
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    // ── DSL migrations: diagnostics-streaming scenarios ──────────────────────

    /// <summary>
    /// Scenario: TUI diagnostics panel displays agent log records in near real-time
    /// A live subscriber receives warning-level records immediately after they are added,
    /// without needing to poll the snapshot — simulating near real-time streaming to the TUI.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void DiagnosticsPanel_WhenAgentEmitsWarningRecord_SubscriberReceivesItImmediately()
    {
        var store = CreateStore(capacity: 10, minimumLevel: "Information");
        var (reader, writer) = store.Subscribe(JobId);

        var warningRecord = MakeRecord("Warning", "agent warning during migration");
        store.Add(JobId, new[] { warningRecord });

        Assert.IsTrue(reader.TryRead(out var streamed),
            "Expected the diagnostics subscriber to receive the warning record immediately.");
        Assert.AreEqual("Warning", streamed.Level);
        Assert.AreEqual("agent warning during migration", streamed.Message);

        store.Unsubscribe(JobId, writer);
    }

    /// <summary>
    /// Scenario: TUI diagnostics panel supports level filter toggle
    /// When the operator changes the level filter from Warning to Information,
    /// subsequent records at Information level and above appear in the snapshot.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void DiagnosticsPanel_WhenLevelFilterChangedToInformation_ShowsInformationAndAbove()
    {
        var store = CreateStore(capacity: 10, minimumLevel: "Information");

        store.Add(JobId, new[]
        {
            MakeRecord("Information", "info record"),
            MakeRecord("Warning", "warning record"),
            MakeRecord("Error", "error record"),
        });

        // Initial filter: Warning (simulates panel showing Warning-level records)
        var warningSnapshot = store.GetSnapshot(JobId, LogLevel.Warning);
        Assert.AreEqual(2, warningSnapshot.Count, "Expect only Warning and Error at Warning filter.");

        // Operator changes filter to Information — expect all records
        var infoSnapshot = store.GetSnapshot(JobId, LogLevel.Information);
        Assert.AreEqual(3, infoSnapshot.Count, "Expect all records visible at Information filter.");
        CollectionAssert.AreEqual(
            new[] { "info record", "warning record", "error record" },
            infoSnapshot.Select(r => r.Message).ToArray());
    }

    /// <summary>
    /// Scenario: TUI diagnostics panel replays recent records on reconnect
    /// After a reconnection, the diagnostics panel replays recent records
    /// from the control plane ring buffer via GetSnapshot.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void DiagnosticsPanel_WhenReconnected_ReplaysRecentRecordsFromRingBuffer()
    {
        var store = CreateStore(capacity: 3, minimumLevel: "Information");

        store.Add(JobId, new[]
        {
            MakeRecord("Information", "record-1"),
            MakeRecord("Warning", "record-2"),
            MakeRecord("Error", "record-3"),
        });

        // Simulate reconnect: client calls GetSnapshot to replay buffered records
        var replayed = store.GetSnapshot(JobId);

        Assert.AreEqual(3, replayed.Count, "All ring-buffer records should be replayed on reconnect.");
        CollectionAssert.AreEqual(
            new[] { "record-1", "record-2", "record-3" },
            replayed.Select(r => r.Message).ToArray());
    }

    /// <summary>
    /// Scenario: Standalone mode aligns control plane minimum with operator level
    /// When an operator runs export with "--level Information" in standalone mode, the control plane
    /// deployment-level minimum is configured to "Information".  All Information and above records
    /// are accepted by the DiagnosticLogStore (available for live streaming), while records below
    /// Information are discarded on ingestion.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void StandaloneMode_OperatorLevelInformation_ControlPlaneAcceptsInformationAndAbove()
    {
        // Simulate: operator ran export --level Information in standalone mode,
        // so the control plane deployment-level minimum is set to "Information".
        var store = CreateStore(capacity: 10, minimumLevel: "Information");

        store.Add(JobId, new[]
        {
            MakeRecord("Debug", "debug — must be discarded"),
            MakeRecord("Information", "info — must be retained"),
            MakeRecord("Warning", "warning — must be retained"),
            MakeRecord("Error", "error — must be retained"),
        });

        var snapshot = store.GetSnapshot(JobId);

        // Debug is below Information — discarded on ingestion
        Assert.AreEqual(3, snapshot.Count,
            "Expected 3 records (Information/Warning/Error); Debug must be discarded by the control plane minimum.");
        CollectionAssert.AreEqual(
            new[] { "info — must be retained", "warning — must be retained", "error — must be retained" },
            snapshot.Select(r => r.Message).ToArray());
    }

    private static DiagnosticLogStore CreateStore(int capacity, string minimumLevel) =>
        new(Options.Create(new DiagnosticLogStoreOptions
        {
            MaxRecordsPerJob = capacity,
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
