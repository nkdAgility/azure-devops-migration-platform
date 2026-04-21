using System;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Discovery module that counts work items and revisions per project across all configured
/// organisations. Wraps <see cref="IInventoryService"/> and writes <c>discovery-summary.csv</c>
/// to the artefact store. Checkpoints after each project so a 20+ hour run can resume.
/// </summary>
public sealed class InventoryDiscoveryModule : IDiscoveryModule
{
    private const string CursorKey = "Checkpoints/Inventory.cursor.json";
    private const string OutputPath = "discovery-summary.csv";

    private readonly IInventoryServiceFactory _inventoryFactory;
    private readonly ILogger<InventoryDiscoveryModule> _logger;
    private readonly IDiscoveryMetrics? _metrics;

    public string Name => "Inventory";
    public DiscoveryJobType DiscoveryType => DiscoveryJobType.Inventory;

    public InventoryDiscoveryModule(
        IInventoryServiceFactory inventoryFactory,
        ILogger<InventoryDiscoveryModule> logger
        , IDiscoveryMetrics? metrics = null
        )
    {
        _inventoryFactory = inventoryFactory;
        _logger = logger;
        _metrics = metrics;
    }

    public async Task RunAsync(DiscoveryContext context, CancellationToken ct)
    {
        var job = context.Job;
        var store = context.ArtefactStore;
        var state = context.StateStore;
        var sink = context.ProgressSink;

        _logger.LogInformation("Inventory module starting for job {JobId}.", job.JobId);

        // Read checkpoint — resume from last completed project.
        var lastCompleted = await ReadCursorAsync(state, ct).ConfigureAwait(false);
        var skipping = lastCompleted is not null;

        if (skipping)
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogInformation("Resuming inventory after project '{LastCompleted}'.", lastCompleted);

        var inventoryService = _inventoryFactory.Create(job.Organisations, job.Policies);

        var csvBuilder = new StringBuilder();
        csvBuilder.AppendLine("Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,IsComplete,Error");

        var checkpointInterval = TimeSpan.FromSeconds(job.Policies.CheckpointIntervalSeconds);
        var lastCheckpoint = DateTime.UtcNow;

        var metrics = _metrics;
        string? currentOrg = null;
        var orgSw = new Stopwatch();
        var projectSw = new Stopwatch();
        int orgProjectCount = 0;

        await foreach (var evt in inventoryService.RunInventoryAsync(ct).ConfigureAwait(false))
        {
            var projectKey = $"{evt.Url}|{evt.ProjectName}";

            // Forward intermediate heartbeats so the CLI live table updates progressively
            // (e.g. Petrel: 291k work items counted ~200 at a time).
            if (!evt.IsComplete)
            {
                if (!skipping)
                {
                    sink.Emit(new ProgressEvent
                    {
                        Module = Name,
                        Stage = "Progress",
                        LastProcessed = projectKey,
                        TotalWorkItems = evt.WorkItemsCount,
                        RevisionsProcessed = evt.RevisionsCount,
                        AttachmentsProcessed = evt.ReposCount,
                        Message = $"{evt.Url} / {evt.ProjectName}: {evt.WorkItemsCount} work items so far…",
                        Timestamp = DateTimeOffset.UtcNow
                    });
                }
                continue;
            }

            // Skip already-completed projects when resuming.
            if (skipping)
            {
                if (projectKey == lastCompleted)
                    skipping = false;
                continue;
            }

            // Organisation transition tracking.
            if (currentOrg != evt.Url)
            {
                if (currentOrg is not null)
                {
                    orgSw.Stop();
                    var orgCompleteTags = new TagList
                    {
                        { "job.id", job.JobId },
                        { "module", Name },
                        { "organisation.url", currentOrg }
                    };
                    metrics?.SetProjectCount(orgProjectCount, orgCompleteTags);
                    metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, orgCompleteTags);
                    metrics?.OrganisationCompleted(orgCompleteTags);
                }
                currentOrg = evt.Url;
                orgProjectCount = 0;
                orgSw.Restart();
                var orgStartTags = new TagList
                {
                    { "job.id", job.JobId },
                    { "module", Name },
                    { "organisation.url", evt.Url }
                };
                metrics?.OrganisationStarted(orgStartTags);
            }
            projectSw.Restart();

            csvBuilder.AppendLine(
                $"{EscapeCsv(evt.Url)},{EscapeCsv(evt.ProjectName)},{evt.WorkItemsCount},{evt.RevisionsCount},{evt.ReposCount},{evt.IsComplete},{EscapeCsv(evt.Error ?? "")}");

            projectSw.Stop();
            var projectTags = new TagList
            {
                { "job.id", job.JobId },
                { "module", Name },
                { "organisation.url", evt.Url },
                { "project.name", evt.ProjectName }
            };
            metrics?.ProjectStarted(projectTags);
            if (evt.Error is not null)
            {
                metrics?.ProjectFailed(projectTags);
            }
            else
            {
                metrics?.ProjectCompleted(projectTags);
                metrics?.RecordWorkItemsCounted(evt.WorkItemsCount, projectTags);
                metrics?.RecordRevisionsCounted(evt.RevisionsCount, projectTags);
                metrics?.RecordReposCounted(evt.ReposCount, projectTags);
            }
            metrics?.RecordProjectDuration(projectSw.Elapsed.TotalMilliseconds, projectTags);
            orgProjectCount++;

            sink.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = evt.Error is not null ? "Failed" : "Inventory",
                LastProcessed = $"{evt.Url}|{evt.ProjectName}",
                TotalWorkItems = evt.WorkItemsCount,
                RevisionsProcessed = evt.RevisionsCount,
                AttachmentsProcessed = evt.ReposCount,
                Message = evt.Error is not null
                    ? $"{evt.Url} / {evt.ProjectName}: failed — {evt.Error}"
                    : $"{evt.Url} / {evt.ProjectName}: {evt.WorkItemsCount} work items, {evt.RevisionsCount} revisions, {evt.ReposCount} repos",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Checkpoint at configured interval.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await store.WriteAsync(OutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                await WriteCursorAsync(state, projectKey, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                metrics?.RecordCheckpointSaved(new TagList { { "job.id", job.JobId }, { "module", Name } });
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogDebug("Inventory checkpoint saved after project '{ProjectKey}'.", projectKey);
            }
        }

        // Complete final organisation.
        if (currentOrg is not null)
        {
            orgSw.Stop();
            var finalOrgTags = new TagList
            {
                { "job.id", job.JobId },
                { "module", Name },
                { "organisation.url", currentOrg }
            };
            metrics?.SetProjectCount(orgProjectCount, finalOrgTags);
            metrics?.RecordOrganisationDuration(orgSw.Elapsed.TotalMilliseconds, finalOrgTags);
            metrics?.OrganisationCompleted(finalOrgTags);
        }

        // Final write.
        await store.WriteAsync(OutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
        await state.DeleteAsync(CursorKey, ct).ConfigureAwait(false);

        sink.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Completed",
            Message = "Inventory complete.",
            Timestamp = DateTimeOffset.UtcNow
        });

        _logger.LogInformation("Inventory module completed for job {JobId}.", job.JobId);
    }

    private static async Task<string?> ReadCursorAsync(IStateStore state, CancellationToken ct)
    {
        var raw = await state.ReadAsync(CursorKey, ct).ConfigureAwait(false);
        if (raw is null) return null;
        var doc = JsonDocument.Parse(raw);
        return doc.RootElement.TryGetProperty("lastCompleted", out var el) ? el.GetString() : null;
    }

    private static Task WriteCursorAsync(IStateStore state, string lastCompleted, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { lastCompleted });
        return state.WriteAsync(CursorKey, json, ct);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
