using System;
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

    public string Name => "Inventory";
    public DiscoveryJobType DiscoveryType => DiscoveryJobType.Inventory;

    public InventoryDiscoveryModule(
        IInventoryServiceFactory inventoryFactory,
        ILogger<InventoryDiscoveryModule> logger)
    {
        _inventoryFactory = inventoryFactory;
        _logger = logger;
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

        await foreach (var evt in inventoryService.RunInventoryAsync(ct).ConfigureAwait(false))
        {
            if (!evt.IsComplete)
                continue;

            var projectKey = $"{evt.Url}|{evt.ProjectName}";

            // Skip already-completed projects when resuming.
            if (skipping)
            {
                if (projectKey == lastCompleted)
                    skipping = false;
                continue;
            }

            csvBuilder.AppendLine(
                $"{EscapeCsv(evt.Url)},{EscapeCsv(evt.ProjectName)},{evt.WorkItemsCount},{evt.RevisionsCount},{evt.ReposCount},{evt.IsComplete},{EscapeCsv(evt.Error ?? "")}");

            sink.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Inventory",
                Message = $"{evt.Url} / {evt.ProjectName}: {evt.WorkItemsCount} work items",
                Timestamp = DateTimeOffset.UtcNow
            });

            // Checkpoint at configured interval.
            if (DateTime.UtcNow - lastCheckpoint >= checkpointInterval)
            {
                await store.WriteAsync(OutputPath, csvBuilder.ToString(), ct).ConfigureAwait(false);
                await WriteCursorAsync(state, projectKey, ct).ConfigureAwait(false);
                lastCheckpoint = DateTime.UtcNow;
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogDebug("Inventory checkpoint saved after project '{ProjectKey}'.", projectKey);
            }
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
