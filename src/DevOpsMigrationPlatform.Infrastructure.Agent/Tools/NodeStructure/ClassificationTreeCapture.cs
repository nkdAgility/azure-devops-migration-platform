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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Captures the full source classification tree during export and writes
/// <c>Nodes/source-tree.json</c> to the package via <see cref="IArtefactStore"/>.
/// </summary>
public sealed class ClassificationTreeCapture
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
    /// </summary>
    public async Task CaptureAsync(IArtefactStore artefactStore, MigrationEndpointOptions endpoint, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("nodes.export.tree");
        var sw = Stopwatch.StartNew();

        var areaNodes = new List<string>();
        var iterationNodes = new List<IterationNodeEntry>();

        try
        {
            await foreach (var path in _reader.EnumerateAreaNodesAsync(endpoint, ct).ConfigureAwait(false))
                areaNodes.Add(path);

            await foreach (var entry in _reader.EnumerateIterationNodesAsync(endpoint, ct).ConfigureAwait(false))
                iterationNodes.Add(entry);

            sw.Stop();

            var snapshot = new ClassificationTreeSnapshot(areaNodes, iterationNodes);
            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            await artefactStore.WriteAsync(ArtifactPath, json, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[NodeStructure] Source tree captured: {AreaCount} area nodes, {IterCount} iteration nodes in {DurationMs}ms.",
                areaNodes.Count, iterationNodes.Count, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "[NodeStructure] Failed to capture source classification tree.");
            throw;
        }
    }
}
#endif
