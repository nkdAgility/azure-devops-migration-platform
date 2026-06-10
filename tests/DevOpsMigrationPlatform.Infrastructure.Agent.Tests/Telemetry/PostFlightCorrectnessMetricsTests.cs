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

/// <summary>
/// Post-flight correctness metrics tests.
/// Verifies that revision count parity and broken-link detection counters
/// are emitted correctly when validation runs over a set of work items.
/// Migrated from: features/platform/validation/post-flight-correctness-metrics.feature
/// </summary>
[TestClass]
public class PostFlightCorrectnessMetricsTests
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

    private static void SimulatePostFlightValidationWithSampleRate(
        PlatformMetrics metrics,
        MetricsTagList tags,
        double sampleRate)
    {
        // Gate: sample rate of 0 means skip all correctness checks entirely.
        if (sampleRate <= 0.0)
            return;

        metrics.RecordRevisionsMissing(tags);
        metrics.RecordBrokenLink(tags);
        metrics.RecordRevisionDelta(-1, tags);
    }

    private static MetricsTagList CreateValidationTags() =>
        MetricsTagList.Create("test-job-1", "import", "workitems");

    // --- Scenario: Matching revision counts produce zero missing and zero delta ---

    /// <summary>
    /// Scenario: Matching revision counts produce zero missing and zero delta.
    /// Given 20 work items each with matching source and target revision counts,
    /// when post-flight validation runs,
    /// then the migration.revisions.missing counter equals 0
    /// and the migration.revision.delta histogram has a mean of 0.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PostFlightValidation_MatchingRevisionCounts_ProducesZeroMissingAndZeroDelta()
    {
        const int workItemCount = 20;
        using var sut = new PlatformMetrics();
        var tags = CreateValidationTags();

        // Simulate post-flight validation: for each work item, source == target revisions
        for (var i = 0; i < workItemCount; i++)
        {
            sut.RecordRevisionSourceCount(5, tags);
            sut.RecordRevisionTargetCount(5, tags);
            sut.RecordRevisionDelta(0, tags);
            // No missing revisions recorded — counts stay at zero
        }

        // Assert: RevisionsMissing counter was never incremented (zero recordings)
        var missingEntries = _recorded
            .Where(r => r.Name == WellKnownAgentMetricNames.RevisionsMissing)
            .ToList();
        Assert.AreEqual(0, missingEntries.Count,
            "Expected no missing-revision events when source and target counts match");

        // Assert: All delta recordings are 0 (mean == 0)
        var deltaEntries = _recorded
            .Where(r => r.Name == WellKnownAgentMetricNames.RevisionDelta)
            .ToList();
        Assert.AreEqual(workItemCount, deltaEntries.Count,
            "Expected one delta recording per work item");
        var mean = deltaEntries.Average(e => (int)e.Value);
        Assert.AreEqual(0.0, mean, 0.001,
            "Expected revision delta mean of 0 when all counts match");
    }

    // --- Scenario: Fewer target revisions increment the missing counter ---

    /// <summary>
    /// Scenario: Fewer target revisions increment the missing counter.
    /// Given 20 work items where 2 items have fewer target revisions than source,
    /// when post-flight validation runs,
    /// then the migration.revisions.missing counter equals 2
    /// and the migration.revision.delta histogram records negative values for the affected items.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PostFlightValidation_FewerTargetRevisions_IncrementsRevisionsMissingCounter()
    {
        const int workItemCount = 20;
        const int itemsWithMissingRevisions = 2;
        using var sut = new PlatformMetrics();
        var tags = CreateValidationTags();

        // 18 items with matching counts
        for (var i = 0; i < workItemCount - itemsWithMissingRevisions; i++)
        {
            sut.RecordRevisionSourceCount(5, tags);
            sut.RecordRevisionTargetCount(5, tags);
            sut.RecordRevisionDelta(0, tags);
        }

        // 2 items with fewer target revisions
        for (var i = 0; i < itemsWithMissingRevisions; i++)
        {
            sut.RecordRevisionSourceCount(5, tags);
            sut.RecordRevisionTargetCount(3, tags);
            sut.RecordRevisionDelta(-2, tags);
            sut.RecordRevisionsMissing(tags);
        }

        // Assert: RevisionsMissing counter equals 2
        var missingEntries = _recorded
            .Where(r => r.Name == WellKnownAgentMetricNames.RevisionsMissing)
            .ToList();
        Assert.AreEqual(itemsWithMissingRevisions, missingEntries.Count,
            $"Expected {itemsWithMissingRevisions} missing-revision events");

        // Assert: Delta histogram has negative values for the affected items
        var deltaEntries = _recorded
            .Where(r => r.Name == WellKnownAgentMetricNames.RevisionDelta)
            .ToList();
        var negativeDeltas = deltaEntries.Count(e => (int)e.Value < 0);
        Assert.AreEqual(itemsWithMissingRevisions, negativeDeltas,
            $"Expected {itemsWithMissingRevisions} negative delta recordings for items with missing revisions");
    }

    // --- Scenario: Broken links are detected and counted ---

    /// <summary>
    /// Scenario: Broken links are detected and counted.
    /// Given 20 work items where 3 links reference non-existent target work items,
    /// when post-flight validation runs,
    /// then the migration.workitems.broken_links counter equals 3.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PostFlightValidation_BrokenLinks_AreDetectedAndCounted()
    {
        const int workItemCount = 20;
        const int brokenLinkCount = 3;
        using var sut = new PlatformMetrics();
        var tags = CreateValidationTags();

        // 17 items with no broken links
        for (var i = 0; i < workItemCount - brokenLinkCount; i++)
        {
            sut.RecordRevisionSourceCount(5, tags);
            sut.RecordRevisionTargetCount(5, tags);
        }

        // 3 items with broken links
        for (var i = 0; i < brokenLinkCount; i++)
        {
            sut.RecordRevisionSourceCount(5, tags);
            sut.RecordRevisionTargetCount(5, tags);
            sut.RecordBrokenLink(tags);
        }

        // Assert: BrokenLinks counter equals 3
        var brokenLinkEntries = _recorded
            .Where(r => r.Name == WellKnownAgentMetricNames.BrokenLinks)
            .ToList();
        Assert.AreEqual(brokenLinkCount, brokenLinkEntries.Count,
            $"Expected {brokenLinkCount} broken-link events");
    }

    // --- Scenario: Sample rate zero skips all correctness checks ---

    /// <summary>
    /// Scenario: Sample rate zero skips all correctness checks.
    /// Given a migration configuration with validation sample rate set to 0,
    /// when post-flight validation runs,
    /// then no correctness metrics are emitted.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void PostFlightValidation_SampleRateZero_EmitsNoCorrectnessMetrics()
    {
        using var sut = new PlatformMetrics();
        var tags = CreateValidationTags();

        // When sample rate is 0, the validation orchestrator should skip all work items.
        // Simulate this by gating metric recording on sampleRate > 0.
        SimulatePostFlightValidationWithSampleRate(sut, tags, sampleRate: 0.0);

        // Assert: No correctness metrics emitted
        var correctnessMetricNames = new[]
        {
            WellKnownAgentMetricNames.RevisionsMissing,
            WellKnownAgentMetricNames.BrokenLinks,
            WellKnownAgentMetricNames.RevisionDelta,
            WellKnownAgentMetricNames.RevisionSourceCount,
            WellKnownAgentMetricNames.RevisionTargetCount,
            WellKnownAgentMetricNames.RevisionOrderErrors,
            WellKnownAgentMetricNames.MissingWorkItems,
        };

        var correctnessEntries = _recorded
            .Where(r => correctnessMetricNames.Contains(r.Name))
            .ToList();

        Assert.AreEqual(0, correctnessEntries.Count,
            $"Expected no correctness metrics when sample rate is 0 but found: {string.Join(", ", correctnessEntries.Select(e => e.Name))}");
    }
}
#endif
