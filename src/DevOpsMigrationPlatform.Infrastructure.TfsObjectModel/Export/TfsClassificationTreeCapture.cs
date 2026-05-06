// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Streaming;

using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;

/// <summary>
/// TFS Object Model implementation of <see cref="IClassificationTreeCapture"/>.
/// Reads from the active job's <see cref="IClassificationTreeReader"/> via
/// <see cref="ActiveTfsJobServices"/> and writes <c>Nodes/source-tree.json</c>
/// to the package via <see cref="IArtefactStore"/>.
/// </summary>
public sealed class TfsClassificationTreeCapture : IClassificationTreeCapture
{
    private const string ArtifactPath = "Nodes/source-tree.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ActiveTfsJobServices _activeServices;
    private readonly ILogger<TfsClassificationTreeCapture> _logger;

    public TfsClassificationTreeCapture(
        ActiveTfsJobServices activeServices,
        ILogger<TfsClassificationTreeCapture> logger)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<int> CaptureAsync(
        IArtefactStore artefactStore,
        CancellationToken ct,
        IPlatformMetrics? metrics = null,
        string? jobId = null,
        IProgressSink? sink = null,
        string moduleName = "Nodes")
    {
        var services = _activeServices.Require();
        var reader = services.ClassificationTreeReader;

        _logger.LogInformation("[TfsNodes] Capturing classification tree.");

        var tags = MetricsTagList.Create(jobId ?? string.Empty, "export", "NodeTranslation");
        var areaNodes = new List<string>();
        var iterationNodes = new List<IterationNodeEntry>();

        await foreach (var path in reader.EnumerateAreaNodesAsync(ct).ConfigureAwait(false))
        {
            areaNodes.Add(path);
            sink?.Emit(new ProgressEvent { Module = moduleName, Stage = "Nodes.Export.AreaNode", Message = path });
        }

        await foreach (var entry in reader.EnumerateIterationNodesAsync(ct).ConfigureAwait(false))
        {
            iterationNodes.Add(entry);
            sink?.Emit(new ProgressEvent { Module = moduleName, Stage = "Nodes.Export.IterationNode", Message = entry.Path });
        }

        var snapshot = new { areaNodes, iterationNodes };
        var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
        await artefactStore.WriteAsync(ArtifactPath, json, ct).ConfigureAwait(false);

        var total = areaNodes.Count + iterationNodes.Count;
        metrics?.RecordNodeExportTreeCount(total, tags);

        _logger.LogInformation(
            "[TfsNodes] Classification tree captured: {AreaCount} area nodes, {IterCount} iteration nodes.",
            areaNodes.Count, iterationNodes.Count);

        return total;
    }
}

