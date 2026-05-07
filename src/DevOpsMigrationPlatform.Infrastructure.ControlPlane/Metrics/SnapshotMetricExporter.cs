// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.ControlPlane.Metrics;

/// <summary>
/// OTel SDK <see cref="BaseExporter{T}"/> that converts aggregated <see cref="Metric"/> batches
/// into a <see cref="JobMetrics"/> and writes it to the <see cref="IJobMetricsStore"/>.
/// Registered alongside OTLP and Azure Monitor exporters through a single
/// <see cref="PeriodicExportingMetricReader"/> — all exporters share the same aggregation cycle.
/// </summary>
internal sealed class SnapshotMetricExporter : BaseExporter<Metric>
{
    private readonly IJobMetricsStore _store;

    public SnapshotMetricExporter(IJobMetricsStore store)
    {
        _store = store;
    }

    public override ExportResult Export(in Batch<Metric> batch)
    {
        // Execution
        long attempted = 0, completed = 0, failed = 0;
        double? durationMeanMs = null;

        // Payload
        double? fieldCountMean = null, attachmentCountMean = null, linkCountMean = null;
        double? revisionCountMean = null, payloadBytesMean = null;

        // Correctness
        long revMissing = 0, revOrderErrors = 0, brokenLinks = 0, missingWI = 0;

        // In-Flight
        int inFlight = 0, queueDepth = 0;

        // Teams
        long teamsExported = 0, teamsImported = 0, teamsFailed = 0, teamsMembers = 0, teamsIterations = 0;

        // Nodes
        long nodesExported = 0, nodesAreaReplicated = 0, nodesIterationReplicated = 0, nodesFailed = 0;

        // Identities
        long identitiesExported = 0, identitiesResolved = 0, identitiesUnresolved = 0, identitiesFailed = 0;
        long inventoryWorkItems = 0, inventoryIdentities = 0, inventoryNodes = 0, inventoryTeams = 0;
        long prepareWorkItemsResolved = 0, prepareWorkItemsUnresolved = 0;
        long prepareIdentitiesResolved = 0, prepareIdentitiesUnresolved = 0;
        long prepareNodesResolved = 0, prepareNodesUnresolved = 0;
        long prepareTeamsResolved = 0, prepareTeamsUnresolved = 0;
        long dependencyWorkItemsAnalysed = 0, dependencyLinksFound = 0, dependenciesCaptureCount = 0;

        foreach (var metric in batch)
        {
            switch (metric.Name)
            {
                // --- Execution ---
                case WellKnownAgentMetricNames.WorkItemsAttempted:
                    attempted = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.WorkItemsCompleted:
                    completed = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.WorkItemsFailed:
                    failed = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.WorkItemDurationMs:
                    durationMeanMs = ReadHistogramMean(metric);
                    break;

                // --- Payload ---
                case WellKnownAgentMetricNames.FieldCount:
                    fieldCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownAgentMetricNames.AttachmentCount:
                    attachmentCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownAgentMetricNames.LinkCount:
                    linkCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownAgentMetricNames.RevisionCount:
                    revisionCountMean = ReadHistogramMean(metric);
                    break;
                case WellKnownAgentMetricNames.PayloadBytes:
                    payloadBytesMean = ReadHistogramMean(metric);
                    break;

                // --- Correctness ---
                case WellKnownAgentMetricNames.RevisionsMissing:
                    revMissing = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.RevisionOrderErrors:
                    revOrderErrors = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.BrokenLinks:
                    brokenLinks = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.MissingWorkItems:
                    missingWI = ReadCounterSum(metric);
                    break;

                // --- In-Flight ---
                case WellKnownAgentMetricNames.WorkItemsInFlight:
                    inFlight = (int)ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.QueueDepth:
                    queueDepth = ReadGaugeLatest(metric);
                    break;

                // --- Teams ---
                case WellKnownAgentMetricNames.TeamsExportCount:
                    teamsExported = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsImportCount:
                    teamsImported = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsExportErrors:
                    teamsFailed += ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsImportErrors:
                    teamsFailed += ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsImportMembersCount:
                    teamsMembers = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsImportIterationsCount:
                    teamsIterations = ReadCounterSum(metric);
                    break;

                // --- Nodes ---
                case WellKnownAgentMetricNames.NodeExportDiscoverCount:
                    nodesExported = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.NodeImportReplicateAreaCount:
                    nodesAreaReplicated = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.NodeImportReplicateIterationCount:
                    nodesIterationReplicated = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.NodeImportReplicateErrors:
                    nodesFailed = ReadCounterSum(metric);
                    break;

                // --- Identities ---
                case WellKnownAgentMetricNames.IdentitiesExportCount:
                    identitiesExported = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesImportResolved:
                    identitiesResolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesImportUnresolved:
                    identitiesUnresolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesExportErrors:
                    identitiesFailed += ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesImportErrors:
                    identitiesFailed += ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.InventoryWorkItems:
                    inventoryWorkItems = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.InventoryIdentities:
                    inventoryIdentities = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.InventoryNodes:
                    inventoryNodes = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.InventoryTeams:
                    inventoryTeams = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.WorkItemsPrepareResolved:
                    prepareWorkItemsResolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.WorkItemsPrepareUnresolved:
                    prepareWorkItemsUnresolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesPrepareResolved:
                    prepareIdentitiesResolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.IdentitiesPrepareUnresolved:
                    prepareIdentitiesUnresolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.NodesPrepareResolved:
                    prepareNodesResolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.NodesPrepareUnresolved:
                    prepareNodesUnresolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsPrepareResolved:
                    prepareTeamsResolved = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.TeamsPrepareUnresolved:
                    prepareTeamsUnresolved = ReadCounterSum(metric);
                    break;

                // --- Dependencies ---
                case WellKnownAgentMetricNames.DependencyWorkItemsAnalysed:
                    dependencyWorkItemsAnalysed = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.DependencyLinks:
                    dependencyLinksFound = ReadCounterSum(metric);
                    break;
                case WellKnownAgentMetricNames.DependenciesCaptureCount:
                    dependenciesCaptureCount = ReadCounterSum(metric);
                    break;
            }
        }

        var teamsCounters = teamsExported > 0 || teamsImported > 0 || teamsFailed > 0
            ? new TeamsCounters
            {
                Exported = teamsExported,
                Imported = teamsImported,
                Failed = teamsFailed,
                Members = teamsMembers,
                Iterations = teamsIterations,
            }
            : null;

        var nodesCounters = nodesExported > 0 || nodesAreaReplicated > 0 || nodesIterationReplicated > 0 || nodesFailed > 0
            ? new NodesCounters
            {
                Exported = nodesExported,
                AreaPathsReplicated = nodesAreaReplicated,
                IterationPathsReplicated = nodesIterationReplicated,
                Failed = nodesFailed,
            }
            : null;

        var identitiesCounters = identitiesExported > 0 || identitiesResolved > 0 || identitiesUnresolved > 0
            ? new IdentitiesCounters
            {
                Exported = identitiesExported,
                Resolved = identitiesResolved,
                Unresolved = identitiesUnresolved,
                Failed = identitiesFailed,
            }
            : null;

        var dependencyCaptureCounters = dependencyWorkItemsAnalysed > 0 || dependencyLinksFound > 0 || dependenciesCaptureCount > 0
            ? new DependencyCounters
            {
                // Prefer exact analysed count; fall back to capture completion count when analysed count isn't emitted.
                WorkItemsAnalysed = dependencyWorkItemsAnalysed > 0 ? dependencyWorkItemsAnalysed : dependenciesCaptureCount,
                ExternalLinksFound = dependencyLinksFound,
            }
            : null;

        _store.Update(new JobMetrics
        {
            Timestamp = DateTimeOffset.UtcNow,
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters
                {
                    Attempted = attempted,
                    Completed = completed,
                    Failed = failed,
                },
                Teams = teamsCounters,
                Nodes = nodesCounters,
                Identities = identitiesCounters,
                DependencyCapture = dependencyCaptureCounters,
                Inventory = new ModulePhaseCounters
                {
                    Completed = inventoryWorkItems + inventoryIdentities + inventoryNodes + inventoryTeams
                },
                Prepare = new ModulePhaseCounters
                {
                    Completed = prepareWorkItemsResolved + prepareIdentitiesResolved + prepareNodesResolved + prepareTeamsResolved,
                    Unresolved = prepareWorkItemsUnresolved + prepareIdentitiesUnresolved + prepareNodesUnresolved + prepareTeamsUnresolved
                },
                Diagnostics = new MigrationDiagnostics
                {
                    WorkItemDurationMeanMs = durationMeanMs,
                    FieldCountMean = fieldCountMean,
                    AttachmentCountMean = attachmentCountMean,
                    LinkCountMean = linkCountMean,
                    RevisionCountMean = revisionCountMean,
                    PayloadBytesMean = payloadBytesMean,
                    RevisionsMissing = revMissing,
                    RevisionOrderErrors = revOrderErrors,
                    BrokenLinks = brokenLinks,
                    MissingWorkItems = missingWI,
                    WorkItemsInFlight = inFlight,
                    QueueDepth = queueDepth,
                },
            },
        });

        return ExportResult.Success;
    }

    // Sums all MetricPoints for a cumulative or delta counter.
    private static long ReadCounterSum(Metric metric)
    {
        long sum = 0;
        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
            sum += mp.GetSumLong();
        return sum;
    }

    // Returns sum(count * sum) / total_count across all MetricPoints for a histogram,
    // providing a weighted mean. Returns null when no measurements have been recorded.
    private static double? ReadHistogramMean(Metric metric)
    {
        double totalSum = 0;
        long totalCount = 0;

        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
        {
            totalSum += mp.GetHistogramSum();
            totalCount += mp.GetHistogramCount();
        }

        return totalCount > 0 ? totalSum / totalCount : null;
    }

    // Reads the latest reported value from an ObservableGauge instrument.
    // The SDK invokes the gauge callback on each collection cycle and reports
    // the value as a sum across MetricPoints.
    private static int ReadGaugeLatest(Metric metric)
    {
        long latest = 0;
        foreach (ref readonly MetricPoint mp in metric.GetMetricPoints())
            latest = mp.GetSumLong();
        return (int)latest;
    }
}
#endif
