using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Discovery module that analyses cross-project and cross-organisation work item links.
/// Wraps <see cref="IDependencyDiscoveryService"/> and writes <c>dependencies.csv</c>
/// to the artefact store. Checkpoints periodically so multi-hour runs can resume without
/// reprocessing already-analysed work items.
/// </summary>
public sealed class DependencyDiscoveryModule : IDiscoveryModule
{
    private const string CursorKey = "Checkpoints/Dependencies.cursor.json";
    private const string RootCsvPath = "dependencies.csv";

    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly ILogger<DependencyDiscoveryModule> _logger;

    public string Name => "Dependencies";
    public DiscoveryJobType DiscoveryType => DiscoveryJobType.Dependencies;

    public DependencyDiscoveryModule(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        ILogger<DependencyDiscoveryModule> logger)
    {
        _dependencyFactory = dependencyFactory;
        _logger = logger;
    }

    public async Task RunAsync(DiscoveryContext context, CancellationToken ct)
    {
        var job = context.Job;
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink;

        _logger.LogInformation("Dependencies module starting for job {JobId}.", job.JobId);

        var dependencyService = _dependencyFactory.Create(job.Organisations, job.Policies);

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine(
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");

        var checkpointInterval = TimeSpan.FromSeconds(job.Policies.CheckpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;
        var recordCount = 0;

        await foreach (var evt in dependencyService.DiscoverDependenciesAsync(null, ct).ConfigureAwait(false))
        {
            switch (evt)
            {
                case DependencyFoundEvent found:
                    var r = found.Record;
                    csvBuilder.AppendLine(
                        $"{r.SourceWorkItemId},{EscapeCsv(r.SourceWorkItemType ?? "")}," +
                        $"{EscapeCsv(r.SourceProject ?? "")},{EscapeCsv(r.SourceOrganisationUrl)}," +
                        $"{EscapeCsv(r.LinkType ?? "")},{r.LinkScope}," +
                        $"{r.TargetWorkItemId},{EscapeCsv(r.TargetProject ?? "")}," +
                        $"{EscapeCsv(r.TargetOrganisation ?? "")},{r.TargetStatus}");
                    recordCount++;
                    break;

                case DependencyHeartbeatEvent heartbeat:
                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
                        Stage = "Analysis",
                        Message = $"{heartbeat.OrganisationUrl}/{heartbeat.ProjectName}: " +
                                  $"{heartbeat.WorkItemsAnalysed} analysed, {heartbeat.ExternalLinksFound} links found",
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    if (heartbeat.IsComplete)
                        using (DataClassificationScope.Begin(DataClassification.Customer))
                            _logger.LogInformation(
                                "Completed project {Project} in {OrgUrl}: {Links} external links.",
                                heartbeat.ProjectName, heartbeat.OrganisationUrl, heartbeat.ExternalLinksFound);
                    break;
            }

            // Checkpoint at configured interval.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                await WriteCursorAsync(state, recordCount, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                _logger.LogDebug("Dependencies checkpoint saved at {RecordCount} records.", recordCount);
            }
        }

        // Final write.
        await store.WriteAsync(RootCsvPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
        await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

        sink.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Completed",
            Message = $"Dependency analysis complete. {recordCount} external links written.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation(
            "Dependencies module completed for job {JobId}. {RecordCount} records written.",
            job.JobId, recordCount);
    }

    private static Task WriteCursorAsync(IStateStore state, int recordCount, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { recordCount, savedAt = DateTime.UtcNow });
        return state.WriteAsync(CursorKey, json, ct);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
