// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Unified implementation of <see cref="IPlatformMetrics"/> that registers all agent instruments
/// under the <see cref="WellKnownMeterNames.Agent"/> meter.
/// Thread-safe: all OTel instrument operations are lock-free.
/// </summary>
public sealed class PlatformMetrics : IPlatformMetrics, IDisposable
{
    private readonly Meter _meter;

    // --- Organisation ---
    private readonly UpDownCounter<int> _organisationsQueued;
    private readonly Counter<long> _organisationsCompleted;
    private readonly Counter<long> _organisationsFailed;
    private readonly Histogram<double> _organisationDuration;
    private int _lastProjectCount;

    // --- Project ---
    private readonly UpDownCounter<int> _projectsQueued;
    private readonly Counter<long> _projectsCompleted;
    private readonly Counter<long> _projectsFailed;
    private readonly Histogram<double> _projectDuration;

    // --- WorkItems Inventory ---
    private readonly Counter<long> _inventoryWorkItems;
    private readonly Histogram<double> _inventoryWorkItemsDuration;
    private readonly Counter<long> _inventoryWorkItemsErrors;
    private readonly Counter<long> _inventoryRevisions;
    private readonly Counter<long> _inventoryRepos;

    // --- Identities / Nodes / Teams Inventory ---
    private readonly Counter<long> _inventoryIdentities;
    private readonly Counter<long> _inventoryNodes;
    private readonly Counter<long> _inventoryTeams;
    private readonly Counter<long> _inventoryConsolidated;
    private readonly Histogram<double> _inventoryConsolidatedDuration;
    private readonly Counter<long> _inventoryConsolidatedErrors;

    // --- Dependencies ---
    private readonly Counter<long> _dependencyLinks;
    private readonly Counter<long> _dependencyWorkItemsAnalysed;
    private readonly Histogram<double> _dependenciesAnalyseDuration;
    private readonly Counter<long> _dependenciesAnalyseErrors;

    // --- Operational ---
    private readonly Counter<long> _checkpointsSaved;
    private readonly Histogram<double> _jobDuration;
    private int _activeJobs;

    // --- WorkItems Export ---
    private readonly Counter<long> _attempted;
    private readonly Counter<long> _completed;
    private readonly Counter<long> _failed;
    private readonly Counter<long> _retried;
    private readonly Histogram<double> _duration;

    // --- WorkItems Payload / Complexity ---
    private readonly Histogram<int> _fieldCount;
    private readonly Histogram<int> _attachmentCount;
    private readonly Histogram<double> _attachmentDownloadDuration;
    private readonly Histogram<long> _attachmentDownloadBytes;
    private readonly Histogram<int> _linkCount;
    private readonly Histogram<int> _revisionCount;
    private readonly Histogram<long> _payloadBytes;

    // --- WorkItems Validate ---
    private readonly Histogram<int> _revisionSourceCount;
    private readonly Histogram<int> _revisionTargetCount;
    private readonly Histogram<int> _revisionDelta;
    private readonly Counter<long> _revisionsMissing;
    private readonly Counter<long> _revisionOrderErrors;
    private readonly Counter<long> _brokenLinks;
    private readonly Counter<long> _missingWorkItems;

    // --- WorkItems In-Flight ---
    private readonly UpDownCounter<int> _inFlight;

    // --- WorkItems Transform ---
    private readonly Counter<long> _fieldTransformApplied;
    private readonly Histogram<double> _fieldTransformDuration;
    private readonly Counter<long> _fieldTransformErrors;
    private readonly UpDownCounter<long> _fieldTransformInFlight;
    private readonly Histogram<int> _fieldTransformFieldsModified;

    // --- WorkItems Idempotency ---
    private readonly Counter<long> _duplicated;
    private readonly Counter<long> _changedOnRerun;
    private readonly Counter<long> _reprocessedAfterResume;
    private readonly Counter<long> _duplicatedAfterResume;
    private readonly Counter<long> _missingAfterResume;

    // --- Nodes Export ---
    private readonly Histogram<int> _nodeExportTreeCount;
    private readonly Histogram<double> _nodeExportTreeDuration;
    private readonly Counter<long> _nodeExportTreeErrors;

    // --- Nodes Translate ---
    private readonly Counter<long> _nodeTranslateCount;
    private readonly Counter<long> _nodeTranslateMapHit;
    private readonly Counter<long> _nodeTranslateAutoSwapHit;
    private readonly Counter<long> _nodeTranslateExternal;
    private readonly Counter<long> _nodeTranslateUnresolvable;

    // --- Nodes Import: Replicate ---
    private readonly Counter<long> _nodeImportReplicateCount;
    private readonly Counter<long> _nodeImportReplicateAreaCount;
    private readonly Counter<long> _nodeImportReplicateIterationCount;
    private readonly Histogram<double> _nodeImportReplicateDuration;
    private readonly Counter<long> _nodeImportReplicateErrors;
    private readonly Counter<long> _nodeImportReplicateSkipped;
    private readonly UpDownCounter<long> _nodeImportReplicateInFlight;

    // --- Nodes Import: PreCollect ---
    private readonly Counter<long> _nodeImportPreCollectCount;
    private readonly Histogram<double> _nodeImportPreCollectDuration;
    private readonly Counter<long> _nodeImportPreCollectErrors;
    private readonly UpDownCounter<long> _nodeImportPreCollectInFlight;

    // --- Teams Export ---
    private readonly Counter<long> _teamExportCount;
    private readonly Histogram<double> _teamExportDuration;
    private readonly Counter<long> _teamExportErrors;
    private readonly UpDownCounter<long> _teamExportInFlight;

    // --- Teams Import ---
    private readonly Counter<long> _teamImportCount;
    private readonly Histogram<double> _teamImportDuration;
    private readonly Counter<long> _teamImportErrors;
    private readonly UpDownCounter<long> _teamImportInFlight;
    private readonly Counter<long> _teamImportMembersCount;
    private readonly Counter<long> _teamImportMembersUnresolved;
    private readonly Counter<long> _teamImportIterationsCount;
    private readonly Counter<long> _teamImportIterationsUnresolvable;
    private readonly Counter<long> _teamImportCapacityCount;
    private readonly Histogram<double> _teamImportExtensionDuration;

    // --- Teams Validate ---
    private readonly Counter<long> _teamValidateCount;
    private readonly Counter<long> _teamValidateErrors;

    // --- Identities Export ---
    private readonly Counter<long> _identityExportCount;
    private readonly Histogram<double> _identityExportDuration;
    private readonly Counter<long> _identityExportErrors;
    private readonly UpDownCounter<long> _identityExportInFlight;

    // --- Identities Import ---
    private readonly Counter<long> _identityImportResolved;
    private readonly Counter<long> _identityImportUnresolved;
    private readonly Histogram<double> _identityImportDuration;
    private readonly Counter<long> _identityImportErrors;

    // --- Identities Validate ---
    private readonly Counter<long> _identityValidateCount;
    private readonly Counter<long> _identityValidateErrors;

    // --- Prepare ---
    private readonly Counter<long> _prepareWorkItemsResolved;
    private readonly Counter<long> _prepareWorkItemsUnresolved;
    private readonly Histogram<double> _prepareWorkItemsDuration;
    private readonly Counter<long> _prepareWorkItemsErrors;
    private readonly UpDownCounter<long> _prepareWorkItemsInFlight;
    private readonly Counter<long> _prepareIdentitiesResolved;
    private readonly Counter<long> _prepareIdentitiesUnresolved;
    private readonly Histogram<double> _prepareIdentitiesDuration;
    private readonly Counter<long> _prepareIdentitiesErrors;
    private readonly UpDownCounter<long> _prepareIdentitiesInFlight;
    private readonly Counter<long> _prepareNodesResolved;
    private readonly Counter<long> _prepareNodesUnresolved;
    private readonly Histogram<double> _prepareNodesDuration;
    private readonly Counter<long> _prepareNodesErrors;
    private readonly UpDownCounter<long> _prepareNodesInFlight;
    private readonly Counter<long> _prepareTeamsResolved;
    private readonly Counter<long> _prepareTeamsUnresolved;
    private readonly Histogram<double> _prepareTeamsDuration;
    private readonly Counter<long> _prepareTeamsErrors;
    private readonly UpDownCounter<long> _prepareTeamsInFlight;

    // --- Package Config ---
    private readonly Counter<long> _configWriteCount;
    private readonly Counter<long> _configWriteErrors;
    private readonly Counter<long> _configReadCount;
    private readonly Counter<long> _configReadErrors;
    private readonly Counter<long> _configReadFallbacks;

    // --- Dependencies Capture ---
    private readonly Counter<long> _dependenciesCaptureCount;
    private readonly Histogram<double> _dependenciesCaptureDuration;
    private readonly Counter<long> _dependenciesCaptureErrors;
    private readonly UpDownCounter<long> _dependenciesCaptureInFlight;

    public PlatformMetrics()
    {
        _meter = new Meter(WellKnownMeterNames.Agent, "4.0");

        // Organisation
        _organisationsQueued = _meter.CreateUpDownCounter<int>(
            WellKnownAgentMetricNames.OrganisationsQueued, unit: "{organisation}");
        _organisationsCompleted = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.OrganisationsCompleted, unit: "{organisation}");
        _organisationsFailed = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.OrganisationsFailed, unit: "{organisation}");
        _organisationDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.OrganisationDurationMs, unit: "ms");
        _meter.CreateObservableGauge(
            WellKnownAgentMetricNames.OrganisationProjectCount,
            () => Volatile.Read(ref _lastProjectCount),
            unit: "{project}");

        // Project
        _projectsQueued = _meter.CreateUpDownCounter<int>(
            WellKnownAgentMetricNames.ProjectsQueued, unit: "{project}");
        _projectsCompleted = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.ProjectsCompleted, unit: "{project}");
        _projectsFailed = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.ProjectsFailed, unit: "{project}");
        _projectDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.ProjectDurationMs, unit: "ms");

        // WorkItems Inventory
        _inventoryWorkItems = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryWorkItems, unit: "{work_item}");
        _inventoryWorkItemsDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.InventoryWorkItemsDurationMs, unit: "ms");
        _inventoryWorkItemsErrors = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryWorkItemsErrors, unit: "{error}");
        _inventoryRevisions = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryRevisions, unit: "{revision}");
        _inventoryRepos = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryRepos, unit: "{repo}");

        // Identities / Nodes / Teams Inventory
        _inventoryIdentities = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryIdentities, unit: "{identity}");
        _inventoryNodes = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryNodes, unit: "{node}");
        _inventoryTeams = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryTeams, unit: "{team}");
        _inventoryConsolidated = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryConsolidated, unit: "{item}");
        _inventoryConsolidatedDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.InventoryConsolidatedDurationMs, unit: "ms");
        _inventoryConsolidatedErrors = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.InventoryConsolidatedErrors, unit: "{error}");

        // Dependencies
        _dependencyLinks = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.DependencyLinks, unit: "{link}");
        _dependencyWorkItemsAnalysed = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.DependencyWorkItemsAnalysed, unit: "{work_item}");
        _dependenciesAnalyseDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.DependenciesAnalyseDurationMs, unit: "ms");
        _dependenciesAnalyseErrors = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.DependenciesAnalyseErrors, unit: "{error}");

        // Operational
        _checkpointsSaved = _meter.CreateCounter<long>(
            WellKnownAgentMetricNames.CheckpointsSaved, unit: "{checkpoint}");
        _jobDuration = _meter.CreateHistogram<double>(
            WellKnownAgentMetricNames.JobDurationMs, unit: "ms");
        _meter.CreateObservableGauge(
            WellKnownAgentMetricNames.JobsActive,
            () => Volatile.Read(ref _activeJobs),
            unit: "{job}");

        // WorkItems Export
        _attempted = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsAttempted, unit: "{work_item}");
        _completed = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsCompleted, unit: "{work_item}");
        _failed = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsFailed, unit: "{work_item}");
        _retried = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsRetried, unit: "{work_item}");
        _duration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.WorkItemDurationMs, unit: "ms");

        // WorkItems Payload
        _fieldCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.FieldCount, unit: "{field}");
        _attachmentCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.AttachmentCount, unit: "{attachment}");
        _attachmentDownloadDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.AttachmentDownloadDurationMs, unit: "ms");
        _attachmentDownloadBytes = _meter.CreateHistogram<long>(WellKnownAgentMetricNames.AttachmentDownloadBytes, unit: "By");
        _linkCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.LinkCount, unit: "{link}");
        _revisionCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.RevisionCount, unit: "{revision}");
        _payloadBytes = _meter.CreateHistogram<long>(WellKnownAgentMetricNames.PayloadBytes, unit: "By");

        // WorkItems Validate
        _revisionSourceCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.RevisionSourceCount, unit: "{revision}");
        _revisionTargetCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.RevisionTargetCount, unit: "{revision}");
        _revisionDelta = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.RevisionDelta, unit: "{revision}");
        _revisionsMissing = _meter.CreateCounter<long>(WellKnownAgentMetricNames.RevisionsMissing, unit: "{work_item}");
        _revisionOrderErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.RevisionOrderErrors, unit: "{work_item}");
        _brokenLinks = _meter.CreateCounter<long>(WellKnownAgentMetricNames.BrokenLinks, unit: "{work_item}");
        _missingWorkItems = _meter.CreateCounter<long>(WellKnownAgentMetricNames.MissingWorkItems, unit: "{work_item}");

        // WorkItems In-Flight
        _inFlight = _meter.CreateUpDownCounter<int>(WellKnownAgentMetricNames.WorkItemsInFlight, unit: "{work_item}");

        // WorkItems Transform
        _fieldTransformApplied = _meter.CreateCounter<long>(WellKnownAgentMetricNames.FieldTransformApplyCount, unit: "{revision}");
        _fieldTransformDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.FieldTransformApplyDurationMs, unit: "ms");
        _fieldTransformErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.FieldTransformApplyErrors, unit: "{revision}");
        _fieldTransformInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.FieldTransformApplyInFlight, unit: "{revision}");
        _fieldTransformFieldsModified = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.FieldTransformApplyFieldsModified, unit: "{field}");

        // WorkItems Idempotency
        _duplicated = _meter.CreateCounter<long>(WellKnownAgentMetricNames.Duplicated, unit: "{work_item}");
        _changedOnRerun = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ChangedOnRerun, unit: "{work_item}");
        _reprocessedAfterResume = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ReprocessedAfterResume, unit: "{work_item}");
        _duplicatedAfterResume = _meter.CreateCounter<long>(WellKnownAgentMetricNames.DuplicatedAfterResume, unit: "{work_item}");
        _missingAfterResume = _meter.CreateCounter<long>(WellKnownAgentMetricNames.MissingAfterResume, unit: "{work_item}");

        // Nodes Export
        _nodeExportTreeCount = _meter.CreateHistogram<int>(WellKnownAgentMetricNames.NodeExportTreeCount, unit: "{node}");
        _nodeExportTreeDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.NodeExportTreeDurationMs, unit: "ms");
        _nodeExportTreeErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeExportTreeErrors, unit: "{error}");

        // Nodes Translate
        _nodeTranslateCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeTranslateCount, unit: "{path}");
        _nodeTranslateMapHit = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeTranslateMapHit, unit: "{path}");
        _nodeTranslateAutoSwapHit = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeTranslateAutoSwapHit, unit: "{path}");
        _nodeTranslateExternal = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeTranslateExternal, unit: "{path}");
        _nodeTranslateUnresolvable = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeTranslateUnresolvable, unit: "{path}");

        // Nodes Import: Replicate
        _nodeImportReplicateCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateCount, unit: "{node}");
        _nodeImportReplicateAreaCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateAreaCount, unit: "{node}");
        _nodeImportReplicateIterationCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateIterationCount, unit: "{node}");
        _nodeImportReplicateDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.NodeImportReplicateDurationMs, unit: "ms");
        _nodeImportReplicateErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateErrors, unit: "{error}");
        _nodeImportReplicateSkipped = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateSkipped, unit: "{node}");
        _nodeImportReplicateInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.NodeImportReplicateInFlight, unit: "{node}");

        // Nodes Import: PreCollect
        _nodeImportPreCollectCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportPreCollectCount, unit: "{node}");
        _nodeImportPreCollectDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.NodeImportPreCollectDurationMs, unit: "ms");
        _nodeImportPreCollectErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodeImportPreCollectErrors, unit: "{error}");
        _nodeImportPreCollectInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.NodeImportPreCollectInFlight, unit: "{node}");

        // Teams Export
        _teamExportCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsExportCount, unit: "{team}");
        _teamExportDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.TeamsExportDurationMs, unit: "ms");
        _teamExportErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsExportErrors, unit: "{error}");
        _teamExportInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.TeamsExportInFlight, unit: "{team}");

        // Teams Import
        _teamImportCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportCount, unit: "{team}");
        _teamImportDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.TeamsImportDurationMs, unit: "ms");
        _teamImportErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportErrors, unit: "{error}");
        _teamImportInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.TeamsImportInFlight, unit: "{team}");
        _teamImportMembersCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportMembersCount, unit: "{member}");
        _teamImportMembersUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportMembersUnresolved, unit: "{member}");
        _teamImportIterationsCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportIterationsCount, unit: "{iteration}");
        _teamImportIterationsUnresolvable = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportIterationsUnresolvable, unit: "{iteration}");
        _teamImportCapacityCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsImportCapacityCount, unit: "{entry}");
        _teamImportExtensionDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.TeamsImportExtensionDurationMs, unit: "ms");

        // Teams Validate
        _teamValidateCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsValidateCount, unit: "{team}");
        _teamValidateErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsValidateErrors, unit: "{error}");

        // Identities Export
        _identityExportCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesExportCount, unit: "{identity}");
        _identityExportDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.IdentitiesExportDurationMs, unit: "ms");
        _identityExportErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesExportErrors, unit: "{error}");
        _identityExportInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.IdentitiesExportInFlight, unit: "{identity}");

        // Identities Import
        _identityImportResolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesImportResolved, unit: "{identity}");
        _identityImportUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesImportUnresolved, unit: "{identity}");
        _identityImportDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.IdentitiesImportDurationMs, unit: "ms");
        _identityImportErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesImportErrors, unit: "{error}");

        // Identities Validate
        _identityValidateCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesValidateCount, unit: "{identity}");
        _identityValidateErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesValidateErrors, unit: "{error}");

        // Prepare
        _prepareWorkItemsResolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsPrepareResolved, unit: "{item}");
        _prepareWorkItemsUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsPrepareUnresolved, unit: "{item}");
        _prepareWorkItemsDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.WorkItemsPrepareDurationMs, unit: "ms");
        _prepareWorkItemsErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsPrepareErrors, unit: "{error}");
        _prepareWorkItemsInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.WorkItemsPrepareInFlight, unit: "{item}");
        _prepareIdentitiesResolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesPrepareResolved, unit: "{item}");
        _prepareIdentitiesUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesPrepareUnresolved, unit: "{item}");
        _prepareIdentitiesDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.IdentitiesPrepareDurationMs, unit: "ms");
        _prepareIdentitiesErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.IdentitiesPrepareErrors, unit: "{error}");
        _prepareIdentitiesInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.IdentitiesPrepareInFlight, unit: "{item}");
        _prepareNodesResolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodesPrepareResolved, unit: "{item}");
        _prepareNodesUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodesPrepareUnresolved, unit: "{item}");
        _prepareNodesDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.NodesPrepareDurationMs, unit: "ms");
        _prepareNodesErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.NodesPrepareErrors, unit: "{error}");
        _prepareNodesInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.NodesPrepareInFlight, unit: "{item}");
        _prepareTeamsResolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsPrepareResolved, unit: "{item}");
        _prepareTeamsUnresolved = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsPrepareUnresolved, unit: "{item}");
        _prepareTeamsDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.TeamsPrepareDurationMs, unit: "ms");
        _prepareTeamsErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.TeamsPrepareErrors, unit: "{error}");
        _prepareTeamsInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.TeamsPrepareInFlight, unit: "{item}");

        // Package Config
        _configWriteCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ConfigWriteCount, unit: "{operation}");
        _configWriteErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ConfigWriteErrors, unit: "{error}");
        _configReadCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ConfigReadCount, unit: "{operation}");
        _configReadErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ConfigReadErrors, unit: "{error}");
        _configReadFallbacks = _meter.CreateCounter<long>(WellKnownAgentMetricNames.ConfigReadFallbacks, unit: "{fallback}");

        // Dependencies Capture
        _dependenciesCaptureCount = _meter.CreateCounter<long>(WellKnownAgentMetricNames.DependenciesCaptureCount, unit: "{project}");
        _dependenciesCaptureDuration = _meter.CreateHistogram<double>(WellKnownAgentMetricNames.DependenciesCaptureDurationMs, unit: "ms");
        _dependenciesCaptureErrors = _meter.CreateCounter<long>(WellKnownAgentMetricNames.DependenciesCaptureErrors, unit: "{project}");
        _dependenciesCaptureInFlight = _meter.CreateUpDownCounter<long>(WellKnownAgentMetricNames.DependenciesCaptureInFlight, unit: "{project}");
    }

    private static TagList ToTagList(MetricsTagList tags)
    {
        var tagList = new TagList();
        for (var i = 0; i < tags.Count; i++)
            tagList.Add(tags[i].Key, tags[i].Value);
        return tagList;
    }

    // --- Organisation ---
    public void OrganisationStarted(MetricsTagList tags)
    {
        _organisationsQueued.Add(1, ToTagList(tags));
        Interlocked.Increment(ref _activeJobs);
    }

    public void OrganisationCompleted(MetricsTagList tags)
    {
        _organisationsQueued.Add(-1, ToTagList(tags));
        _organisationsCompleted.Add(1, ToTagList(tags));
    }

    public void OrganisationFailed(MetricsTagList tags)
    {
        _organisationsQueued.Add(-1, ToTagList(tags));
        _organisationsFailed.Add(1, ToTagList(tags));
    }

    public void RecordOrganisationDuration(double milliseconds, MetricsTagList tags)
        => _organisationDuration.Record(milliseconds, ToTagList(tags));

    public void SetProjectCount(int count, MetricsTagList tags)
        => Volatile.Write(ref _lastProjectCount, count);

    // --- Project ---
    public void ProjectStarted(MetricsTagList tags)
        => _projectsQueued.Add(1, ToTagList(tags));

    public void ProjectCompleted(MetricsTagList tags)
    {
        _projectsQueued.Add(-1, ToTagList(tags));
        _projectsCompleted.Add(1, ToTagList(tags));
    }

    public void ProjectFailed(MetricsTagList tags)
    {
        _projectsQueued.Add(-1, ToTagList(tags));
        _projectsFailed.Add(1, ToTagList(tags));
    }

    public void RecordProjectDuration(double milliseconds, MetricsTagList tags)
        => _projectDuration.Record(milliseconds, ToTagList(tags));

    // --- WorkItems Inventory ---
    public void RecordWorkItemsCounted(int count, MetricsTagList tags)
        => _inventoryWorkItems.Add(count, ToTagList(tags));

    public void RecordRevisionsCounted(int count, MetricsTagList tags)
        => _inventoryRevisions.Add(count, ToTagList(tags));

    public void RecordReposCounted(int count, MetricsTagList tags)
        => _inventoryRepos.Add(count, ToTagList(tags));

    public void RecordInventoryWorkItems(int count, MetricsTagList tags)
        => _inventoryWorkItems.Add(count, ToTagList(tags));

    public void RecordInventoryWorkItemsDuration(double milliseconds, MetricsTagList tags)
        => _inventoryWorkItemsDuration.Record(milliseconds, ToTagList(tags));

    public void RecordInventoryWorkItemsErrors(MetricsTagList tags)
        => _inventoryWorkItemsErrors.Add(1, ToTagList(tags));

    public void RecordInventoryIdentities(int count, MetricsTagList tags)
        => _inventoryIdentities.Add(count, ToTagList(tags));

    public void RecordInventoryNodes(int count, MetricsTagList tags)
        => _inventoryNodes.Add(count, ToTagList(tags));

    public void RecordInventoryTeams(int count, MetricsTagList tags)
        => _inventoryTeams.Add(count, ToTagList(tags));

    public void RecordInventoryConsolidated(int count, MetricsTagList tags)
        => _inventoryConsolidated.Add(count, ToTagList(tags));

    public void RecordInventoryConsolidatedDuration(double milliseconds, MetricsTagList tags)
        => _inventoryConsolidatedDuration.Record(milliseconds, ToTagList(tags));

    public void RecordInventoryConsolidatedErrors(MetricsTagList tags)
        => _inventoryConsolidatedErrors.Add(1, ToTagList(tags));

    // --- Dependencies ---
    public void RecordLinksFound(int count, MetricsTagList tags)
        => _dependencyLinks.Add(count, ToTagList(tags));

    public void RecordWorkItemsAnalysed(int count, MetricsTagList tags)
        => _dependencyWorkItemsAnalysed.Add(count, ToTagList(tags));

    public void RecordDependenciesAnalyseDuration(double milliseconds, MetricsTagList tags)
        => _dependenciesAnalyseDuration.Record(milliseconds, ToTagList(tags));

    public void RecordDependenciesAnalyseErrors(MetricsTagList tags)
        => _dependenciesAnalyseErrors.Add(1, ToTagList(tags));

    // --- Operational ---
    public void RecordCheckpointSaved(MetricsTagList tags)
        => _checkpointsSaved.Add(1, ToTagList(tags));

    public void RecordJobDuration(double milliseconds, MetricsTagList tags)
    {
        _jobDuration.Record(milliseconds, ToTagList(tags));
        Interlocked.Decrement(ref _activeJobs);
    }

    // --- WorkItems Export ---
    public void RecordWorkItemAttempted(MetricsTagList tags) => _attempted.Add(1, ToTagList(tags));
    public void RecordWorkItemCompleted(MetricsTagList tags) => _completed.Add(1, ToTagList(tags));
    public void RecordWorkItemFailed(MetricsTagList tags) => _failed.Add(1, ToTagList(tags));
    public void RecordWorkItemRetried(MetricsTagList tags) => _retried.Add(1, ToTagList(tags));
    public void RecordWorkItemDuration(double milliseconds, MetricsTagList tags) => _duration.Record(milliseconds, ToTagList(tags));

    // --- WorkItems Payload ---
    public void RecordFieldCount(int count, MetricsTagList tags) => _fieldCount.Record(count, ToTagList(tags));
    public void RecordAttachmentCount(int count, MetricsTagList tags) => _attachmentCount.Record(count, ToTagList(tags));
    public void RecordAttachmentDownloadDuration(double milliseconds, MetricsTagList tags) => _attachmentDownloadDuration.Record(milliseconds, ToTagList(tags));
    public void RecordAttachmentDownloadBytes(long bytes, MetricsTagList tags) => _attachmentDownloadBytes.Record(bytes, ToTagList(tags));
    public void RecordLinkCount(int count, MetricsTagList tags) => _linkCount.Record(count, ToTagList(tags));
    public void RecordRevisionCount(int count, MetricsTagList tags) => _revisionCount.Record(count, ToTagList(tags));
    public void RecordPayloadBytes(long bytes, MetricsTagList tags) => _payloadBytes.Record(bytes, ToTagList(tags));

    // --- WorkItems Validate ---
    public void RecordRevisionSourceCount(int count, MetricsTagList tags) => _revisionSourceCount.Record(count, ToTagList(tags));
    public void RecordRevisionTargetCount(int count, MetricsTagList tags) => _revisionTargetCount.Record(count, ToTagList(tags));
    public void RecordRevisionDelta(int delta, MetricsTagList tags) => _revisionDelta.Record(delta, ToTagList(tags));
    public void RecordRevisionsMissing(MetricsTagList tags) => _revisionsMissing.Add(1, ToTagList(tags));
    public void RecordRevisionOrderError(MetricsTagList tags) => _revisionOrderErrors.Add(1, ToTagList(tags));
    public void RecordBrokenLink(MetricsTagList tags) => _brokenLinks.Add(1, ToTagList(tags));
    public void RecordMissingWorkItem(MetricsTagList tags) => _missingWorkItems.Add(1, ToTagList(tags));

    // --- WorkItems In-Flight ---
    public void IncrementInFlight(MetricsTagList tags) => _inFlight.Add(1, ToTagList(tags));
    public void DecrementInFlight(MetricsTagList tags) => _inFlight.Add(-1, ToTagList(tags));

    // --- WorkItems Transform ---
    public void RecordFieldTransformApplied(MetricsTagList tags) => _fieldTransformApplied.Add(1, ToTagList(tags));
    public void RecordFieldTransformDuration(double milliseconds, MetricsTagList tags) => _fieldTransformDuration.Record(milliseconds, ToTagList(tags));
    public void RecordFieldTransformError(MetricsTagList tags) => _fieldTransformErrors.Add(1, ToTagList(tags));
    public void IncrementFieldTransformInFlight(MetricsTagList tags) => _fieldTransformInFlight.Add(1, ToTagList(tags));
    public void DecrementFieldTransformInFlight(MetricsTagList tags) => _fieldTransformInFlight.Add(-1, ToTagList(tags));
    public void RecordFieldTransformFieldsModified(int count, MetricsTagList tags) => _fieldTransformFieldsModified.Record(count, ToTagList(tags));

    // --- WorkItems Idempotency ---
    public void RecordDuplicated(MetricsTagList tags) => _duplicated.Add(1, ToTagList(tags));
    public void RecordChangedOnRerun(MetricsTagList tags) => _changedOnRerun.Add(1, ToTagList(tags));
    public void RecordReprocessedAfterResume(MetricsTagList tags) => _reprocessedAfterResume.Add(1, ToTagList(tags));
    public void RecordDuplicatedAfterResume(MetricsTagList tags) => _duplicatedAfterResume.Add(1, ToTagList(tags));
    public void RecordMissingAfterResume(MetricsTagList tags) => _missingAfterResume.Add(1, ToTagList(tags));

    // --- Nodes Export ---
    public void RecordNodeExportTreeCount(int count, MetricsTagList tags) => _nodeExportTreeCount.Record(count, ToTagList(tags));
    public void RecordNodeExportTreeDuration(double milliseconds, MetricsTagList tags) => _nodeExportTreeDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeExportTreeError(MetricsTagList tags) => _nodeExportTreeErrors.Add(1, ToTagList(tags));

    // --- Nodes Translate ---
    public void RecordNodeTranslateCount(MetricsTagList tags) => _nodeTranslateCount.Add(1, ToTagList(tags));
    public void RecordNodeTranslateMapHit(MetricsTagList tags) => _nodeTranslateMapHit.Add(1, ToTagList(tags));
    public void RecordNodeTranslateAutoSwapHit(MetricsTagList tags) => _nodeTranslateAutoSwapHit.Add(1, ToTagList(tags));
    public void RecordNodeTranslateExternal(MetricsTagList tags) => _nodeTranslateExternal.Add(1, ToTagList(tags));
    public void RecordNodeTranslateUnresolvable(MetricsTagList tags) => _nodeTranslateUnresolvable.Add(1, ToTagList(tags));

    // --- Nodes Import: Replicate ---
    public void RecordNodeImportReplicateCount(MetricsTagList tags) => _nodeImportReplicateCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateAreaCount(MetricsTagList tags) => _nodeImportReplicateAreaCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateIterationCount(MetricsTagList tags) => _nodeImportReplicateIterationCount.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateDuration(double milliseconds, MetricsTagList tags) => _nodeImportReplicateDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeImportReplicateError(MetricsTagList tags) => _nodeImportReplicateErrors.Add(1, ToTagList(tags));
    public void RecordNodeImportReplicateSkipped(MetricsTagList tags) => _nodeImportReplicateSkipped.Add(1, ToTagList(tags));
    public void IncrementNodeImportReplicateInFlight(MetricsTagList tags) => _nodeImportReplicateInFlight.Add(1, ToTagList(tags));
    public void DecrementNodeImportReplicateInFlight(MetricsTagList tags) => _nodeImportReplicateInFlight.Add(-1, ToTagList(tags));

    // --- Nodes Import: PreCollect ---
    public void RecordNodeImportPreCollectCount(MetricsTagList tags) => _nodeImportPreCollectCount.Add(1, ToTagList(tags));
    public void RecordNodeImportPreCollectDuration(double milliseconds, MetricsTagList tags) => _nodeImportPreCollectDuration.Record(milliseconds, ToTagList(tags));
    public void RecordNodeImportPreCollectError(MetricsTagList tags) => _nodeImportPreCollectErrors.Add(1, ToTagList(tags));
    public void IncrementNodeImportPreCollectInFlight(MetricsTagList tags) => _nodeImportPreCollectInFlight.Add(1, ToTagList(tags));
    public void DecrementNodeImportPreCollectInFlight(MetricsTagList tags) => _nodeImportPreCollectInFlight.Add(-1, ToTagList(tags));

    // --- Teams Export ---
    public void RecordTeamExportCount(MetricsTagList tags) => _teamExportCount.Add(1, ToTagList(tags));
    public void RecordTeamExportDuration(double milliseconds, MetricsTagList tags) => _teamExportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordTeamExportError(MetricsTagList tags) => _teamExportErrors.Add(1, ToTagList(tags));
    public void IncrementTeamExportInFlight(MetricsTagList tags) => _teamExportInFlight.Add(1, ToTagList(tags));
    public void DecrementTeamExportInFlight(MetricsTagList tags) => _teamExportInFlight.Add(-1, ToTagList(tags));

    // --- Teams Import ---
    public void RecordTeamImportCount(MetricsTagList tags) => _teamImportCount.Add(1, ToTagList(tags));
    public void RecordTeamImportDuration(double milliseconds, MetricsTagList tags) => _teamImportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordTeamImportError(MetricsTagList tags) => _teamImportErrors.Add(1, ToTagList(tags));
    public void IncrementTeamImportInFlight(MetricsTagList tags) => _teamImportInFlight.Add(1, ToTagList(tags));
    public void DecrementTeamImportInFlight(MetricsTagList tags) => _teamImportInFlight.Add(-1, ToTagList(tags));
    public void RecordTeamImportMemberCount(MetricsTagList tags) => _teamImportMembersCount.Add(1, ToTagList(tags));
    public void RecordTeamImportMemberUnresolved(MetricsTagList tags) => _teamImportMembersUnresolved.Add(1, ToTagList(tags));
    public void RecordTeamImportIterationCount(MetricsTagList tags) => _teamImportIterationsCount.Add(1, ToTagList(tags));
    public void RecordTeamImportIterationUnresolvable(MetricsTagList tags) => _teamImportIterationsUnresolvable.Add(1, ToTagList(tags));
    public void RecordTeamImportCapacityCount(MetricsTagList tags) => _teamImportCapacityCount.Add(1, ToTagList(tags));
    public void RecordTeamImportExtensionDuration(double milliseconds, MetricsTagList tags) => _teamImportExtensionDuration.Record(milliseconds, ToTagList(tags));

    // --- Teams Validate ---
    public void RecordTeamValidateCount(MetricsTagList tags) => _teamValidateCount.Add(1, ToTagList(tags));
    public void RecordTeamValidateError(MetricsTagList tags) => _teamValidateErrors.Add(1, ToTagList(tags));

    // --- Identities Export ---
    public void RecordIdentityExportCount(MetricsTagList tags) => _identityExportCount.Add(1, ToTagList(tags));
    public void RecordIdentityExportDuration(double milliseconds, MetricsTagList tags) => _identityExportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordIdentityExportError(MetricsTagList tags) => _identityExportErrors.Add(1, ToTagList(tags));
    public void IncrementIdentityExportInFlight(MetricsTagList tags) => _identityExportInFlight.Add(1, ToTagList(tags));
    public void DecrementIdentityExportInFlight(MetricsTagList tags) => _identityExportInFlight.Add(-1, ToTagList(tags));

    // --- Identities Import ---
    public void RecordIdentityImportResolved(MetricsTagList tags) => _identityImportResolved.Add(1, ToTagList(tags));
    public void RecordIdentityImportUnresolved(MetricsTagList tags) => _identityImportUnresolved.Add(1, ToTagList(tags));
    public void RecordIdentityImportDuration(double milliseconds, MetricsTagList tags) => _identityImportDuration.Record(milliseconds, ToTagList(tags));
    public void RecordIdentityImportError(MetricsTagList tags) => _identityImportErrors.Add(1, ToTagList(tags));

    // --- Identities Validate ---
    public void RecordIdentityValidateCount(MetricsTagList tags) => _identityValidateCount.Add(1, ToTagList(tags));
    public void RecordIdentityValidateError(MetricsTagList tags) => _identityValidateErrors.Add(1, ToTagList(tags));

    // --- WorkItems Prepare ---
    public void RecordPrepareWorkItemsResolved(int count, MetricsTagList tags) => _prepareWorkItemsResolved.Add(count, ToTagList(tags));
    public void RecordPrepareWorkItemsUnresolved(int count, MetricsTagList tags) => _prepareWorkItemsUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareWorkItemsDuration(double milliseconds, MetricsTagList tags) => _prepareWorkItemsDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareWorkItemsError(MetricsTagList tags) => _prepareWorkItemsErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareWorkItemsInFlight(MetricsTagList tags) => _prepareWorkItemsInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareWorkItemsInFlight(MetricsTagList tags) => _prepareWorkItemsInFlight.Add(-1, ToTagList(tags));

    // --- Identities Prepare ---
    public void RecordPrepareIdentitiesResolved(int count, MetricsTagList tags) => _prepareIdentitiesResolved.Add(count, ToTagList(tags));
    public void RecordPrepareIdentitiesUnresolved(int count, MetricsTagList tags) => _prepareIdentitiesUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareIdentitiesDuration(double milliseconds, MetricsTagList tags) => _prepareIdentitiesDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareIdentitiesError(MetricsTagList tags) => _prepareIdentitiesErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareIdentitiesInFlight(MetricsTagList tags) => _prepareIdentitiesInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareIdentitiesInFlight(MetricsTagList tags) => _prepareIdentitiesInFlight.Add(-1, ToTagList(tags));

    // --- Nodes Prepare ---
    public void RecordPrepareNodesResolved(int count, MetricsTagList tags) => _prepareNodesResolved.Add(count, ToTagList(tags));
    public void RecordPrepareNodesUnresolved(int count, MetricsTagList tags) => _prepareNodesUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareNodesDuration(double milliseconds, MetricsTagList tags) => _prepareNodesDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareNodesError(MetricsTagList tags) => _prepareNodesErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareNodesInFlight(MetricsTagList tags) => _prepareNodesInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareNodesInFlight(MetricsTagList tags) => _prepareNodesInFlight.Add(-1, ToTagList(tags));

    // --- Teams Prepare ---
    public void RecordPrepareTeamsResolved(int count, MetricsTagList tags) => _prepareTeamsResolved.Add(count, ToTagList(tags));
    public void RecordPrepareTeamsUnresolved(int count, MetricsTagList tags) => _prepareTeamsUnresolved.Add(count, ToTagList(tags));
    public void RecordPrepareTeamsDuration(double milliseconds, MetricsTagList tags) => _prepareTeamsDuration.Record(milliseconds, ToTagList(tags));
    public void RecordPrepareTeamsError(MetricsTagList tags) => _prepareTeamsErrors.Add(1, ToTagList(tags));
    public void IncrementPrepareTeamsInFlight(MetricsTagList tags) => _prepareTeamsInFlight.Add(1, ToTagList(tags));
    public void DecrementPrepareTeamsInFlight(MetricsTagList tags) => _prepareTeamsInFlight.Add(-1, ToTagList(tags));

    // --- Package Config ---
    public void RecordConfigWriteCompleted(MetricsTagList tags) => _configWriteCount.Add(1, ToTagList(tags));
    public void RecordConfigWriteError(MetricsTagList tags) => _configWriteErrors.Add(1, ToTagList(tags));
    public void RecordConfigReadCompleted(MetricsTagList tags) => _configReadCount.Add(1, ToTagList(tags));
    public void RecordConfigReadError(MetricsTagList tags) => _configReadErrors.Add(1, ToTagList(tags));
    public void RecordConfigReadFallback(MetricsTagList tags) => _configReadFallbacks.Add(1, ToTagList(tags));

    // --- Dependencies Capture ---
    public void DependenciesCaptureStarted(MetricsTagList tags) => _dependenciesCaptureCount.Add(1, ToTagList(tags));
    public void DependenciesCaptureCompleted(MetricsTagList tags) { /* count recorded at DependenciesCaptureStarted */ }
    public void DependenciesCaptureFailed(MetricsTagList tags) => _dependenciesCaptureErrors.Add(1, ToTagList(tags));
    public void RecordDependenciesCaptureDuration(double milliseconds, MetricsTagList tags) => _dependenciesCaptureDuration.Record(milliseconds, ToTagList(tags));
    public void DependenciesCaptureInFlightIncrement(MetricsTagList tags) => _dependenciesCaptureInFlight.Add(1, ToTagList(tags));
    public void DependenciesCaptureInFlightDecrement(MetricsTagList tags) => _dependenciesCaptureInFlight.Add(-1, ToTagList(tags));

    public void Dispose() => _meter.Dispose();
}
