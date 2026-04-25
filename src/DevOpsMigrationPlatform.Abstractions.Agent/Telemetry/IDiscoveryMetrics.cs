using System.Diagnostics;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Recording contract for discovery OTel metric instruments (inventory + dependencies).
/// All methods accept a pre-built <see cref="TagList"/> carrying the mandatory
/// <c>job.id</c> and <c>module</c> dimension tags.
/// </summary>
public interface IDiscoveryMetrics
{
    // --- Organisation ---
    void OrganisationStarted(in TagList tags);
    void OrganisationCompleted(in TagList tags);
    void OrganisationFailed(in TagList tags);
    void RecordOrganisationDuration(double milliseconds, in TagList tags);
    void SetProjectCount(int count, in TagList tags);

    // --- Project ---
    void ProjectStarted(in TagList tags);
    void ProjectCompleted(in TagList tags);
    void ProjectFailed(in TagList tags);
    void RecordProjectDuration(double milliseconds, in TagList tags);

    // --- Inventory ---
    void RecordWorkItemsCounted(int count, in TagList tags);
    void RecordRevisionsCounted(int count, in TagList tags);
    void RecordReposCounted(int count, in TagList tags);

    // --- Dependencies ---
    void RecordLinksFound(int count, in TagList tags);
    void RecordWorkItemsAnalysed(int count, in TagList tags);

    // --- Operational ---
    void RecordCheckpointSaved(in TagList tags);
    void RecordJobDuration(double milliseconds, in TagList tags);
}
