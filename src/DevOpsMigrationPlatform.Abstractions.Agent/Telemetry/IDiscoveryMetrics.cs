// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Recording contract for discovery OTel metric instruments (inventory + dependencies).
/// All methods accept a pre-built <see cref="MetricsTagList"/> carrying the mandatory
/// <c>job.id</c> and <c>module</c> dimension tags.
/// </summary>
public interface IDiscoveryMetrics
{
    // --- Organisation ---
    void OrganisationStarted(MetricsTagList tags);
    void OrganisationCompleted(MetricsTagList tags);
    void OrganisationFailed(MetricsTagList tags);
    void RecordOrganisationDuration(double milliseconds, MetricsTagList tags);
    void SetProjectCount(int count, MetricsTagList tags);

    // --- Project ---
    void ProjectStarted(MetricsTagList tags);
    void ProjectCompleted(MetricsTagList tags);
    void ProjectFailed(MetricsTagList tags);
    void RecordProjectDuration(double milliseconds, MetricsTagList tags);

    // --- Inventory ---
    void RecordWorkItemsCounted(int count, MetricsTagList tags);
    void RecordRevisionsCounted(int count, MetricsTagList tags);
    void RecordReposCounted(int count, MetricsTagList tags);
    void RecordInventoryWorkItems(int count, MetricsTagList tags);
    void RecordInventoryWorkItemsDuration(double milliseconds, MetricsTagList tags);
    void RecordInventoryWorkItemsErrors(MetricsTagList tags);
    void RecordInventoryIdentities(int count, MetricsTagList tags);
    void RecordInventoryNodes(int count, MetricsTagList tags);
    void RecordInventoryTeams(int count, MetricsTagList tags);
    void RecordInventoryConsolidated(int count, MetricsTagList tags);
    void RecordInventoryConsolidatedDuration(double milliseconds, MetricsTagList tags);
    void RecordInventoryConsolidatedErrors(MetricsTagList tags);

    // --- Dependencies ---
    void RecordLinksFound(int count, MetricsTagList tags);
    void RecordWorkItemsAnalysed(int count, MetricsTagList tags);
    void RecordDependenciesAnalyseDuration(double milliseconds, MetricsTagList tags);
    void RecordDependenciesAnalyseErrors(MetricsTagList tags);

    // --- Operational ---
    void RecordCheckpointSaved(MetricsTagList tags);
    void RecordJobDuration(double milliseconds, MetricsTagList tags);
}
