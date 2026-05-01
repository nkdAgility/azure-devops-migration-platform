#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Captures the full source classification tree during export and writes
/// <c>Nodes/source-tree.json</c> to the package via <see cref="IArtefactStore"/>.
/// </summary>
public sealed class ClassificationTreeCapture : IClassificationTreeCapture
{
    private const string ArtifactPath = "Nodes/source-tree.json";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IClassificationTreeReader _reader;
    private readonly ILogger<ClassificationTreeCapture> _logger;

    public ClassificationTreeCapture(
        IClassificationTreeReader reader,
        ILogger<ClassificationTreeCapture> logger)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Captures the source classification tree and writes it to the package.
    /// Emits <c>migration.nodes.export.*</c> OTel metrics.
    /// </summary>
    /// <returns>Total number of nodes captured (area + iteration).</returns>
    public async Task<int> CaptureAsync(
        IArtefactStore artefactStore,
        CancellationToken ct,
        IMigrationMetrics? metrics = null,
        string? jobId = null,
        IProgressSink? sink = null,
        string moduleName = "Nodes")
    {
        using var activity = s_activitySource.StartActivity("nodes.export.tree");
        var sw = Stopwatch.StartNew();

        var tags = MigrationTagList.Create(jobId ?? string.Empty, "export", "NodeTranslation");
        var areaNodes = new List<string>();
        var iterationNodes = new List<IterationNodeEntry>();

        try
        {
            await foreach (var path in _reader.EnumerateAreaNodesAsync(ct).ConfigureAwait(false))
            {
                areaNodes.Add(path);
                sink?.Emit(new ProgressEvent { Module = moduleName, Stage = "Nodes.Export.AreaNode", Message = path });
            }

            await foreach (var entry in _reader.EnumerateIterationNodesAsync(ct).ConfigureAwait(false))
            {
                iterationNodes.Add(entry);
                sink?.Emit(new ProgressEvent { Module = moduleName, Stage = "Nodes.Export.IterationNode", Message = entry.Path });
            }

            sw.Stop();

            var snapshot = new ClassificationTreeSnapshot(areaNodes, iterationNodes);
            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            await artefactStore.WriteAsync(ArtifactPath, json, ct).ConfigureAwait(false);

            var totalNodes = areaNodes.Count + iterationNodes.Count;
            metrics?.RecordNodeExportTreeCount(totalNodes, tags);
            metrics?.RecordNodeExportTreeDuration(sw.Elapsed.TotalMilliseconds, tags);

            activity?.SetTag("nodes.area.count", areaNodes.Count);
            activity?.SetTag("nodes.iteration.count", iterationNodes.Count);

            _logger.LogInformation(
                "[NodeTranslation] Source tree captured: {AreaCount} area nodes, {IterCount} iteration nodes in {DurationMs}ms.",
                areaNodes.Count, iterationNodes.Count, sw.ElapsedMilliseconds);

            return totalNodes;
        }
        catch (Exception ex)
        {
            sw.Stop();
            metrics?.RecordNodeExportTreeError(tags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "[NodeTranslation] Failed to capture source classification tree.");
            throw;
        }
    }
}
#endif
