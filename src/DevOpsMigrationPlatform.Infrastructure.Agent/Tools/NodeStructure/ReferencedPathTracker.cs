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
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Maintains in-memory sets of discovered area and iteration paths during export.
/// Persists to <c>Nodes/referenced-paths.json</c> via <see cref="IArtefactStore"/> on each new discovery.
/// Supports resume: loads existing artifact on initialization.
/// </summary>
public sealed class ReferencedPathTracker
{
    private const string ArtifactPath = "Nodes/referenced-paths.json";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ILogger<ReferencedPathTracker> _logger;
    private readonly HashSet<string> _areaPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _iterationPaths = new(StringComparer.OrdinalIgnoreCase);

    public ReferencedPathTracker(ILogger<ReferencedPathTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Loads existing artifact (for resume). Call once before discovery begins.</summary>
    public async Task InitializeAsync(IArtefactStore artefactStore, CancellationToken ct)
    {
        var json = await artefactStore.ReadAsync(ArtifactPath, ct).ConfigureAwait(false);
        if (json is null) return;

        var existing = JsonSerializer.Deserialize<ReferencedPathsArtifact>(json, s_jsonOptions);
        if (existing is null) return;

        foreach (var p in existing.AreaPaths)
            _areaPaths.Add(p);
        foreach (var p in existing.IterationPaths)
            _iterationPaths.Add(p);

        _logger.LogDebug("[NodeStructure] Loaded {AreaCount} area paths and {IterCount} iteration paths from existing artifact.",
            _areaPaths.Count, _iterationPaths.Count);
    }

    /// <summary>Records a discovered area path. If new, persists the artifact.</summary>
    public async Task RecordAreaPathAsync(string path, IArtefactStore artefactStore, CancellationToken ct)
    {
        if (!_areaPaths.Add(path)) return;
        await PersistAsync(artefactStore, ct).ConfigureAwait(false);
        _logger.LogDebug("[NodeStructure] Area path discovered: {Path}", path);
    }

    /// <summary>Records a discovered iteration path. If new, persists the artifact.</summary>
    public async Task RecordIterationPathAsync(string path, IArtefactStore artefactStore, CancellationToken ct)
    {
        if (!_iterationPaths.Add(path)) return;
        await PersistAsync(artefactStore, ct).ConfigureAwait(false);
        _logger.LogDebug("[NodeStructure] Iteration path discovered: {Path}", path);
    }

    private async Task PersistAsync(IArtefactStore artefactStore, CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("nodes.export.discover");
        var artifact = new ReferencedPathsArtifact(
            new List<string>(_areaPaths),
            new List<string>(_iterationPaths));
        var json = JsonSerializer.Serialize(artifact, s_jsonOptions);
        await artefactStore.WriteAsync(ArtifactPath, json, ct).ConfigureAwait(false);
    }

    /// <summary>Returns current area paths (for testing).</summary>
    public IReadOnlySet<string> AreaPaths => _areaPaths;

    /// <summary>Returns current iteration paths (for testing).</summary>
    public IReadOnlySet<string> IterationPaths => _iterationPaths;
}
#endif
