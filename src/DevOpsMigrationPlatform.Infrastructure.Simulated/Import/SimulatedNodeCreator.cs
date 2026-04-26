using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// Simulated in-memory implementation of <see cref="INodeCreator"/>.
/// Tracks created nodes per project in a thread-safe dictionary.
/// All operations are immediately reflected in memory — no external I/O.
/// </summary>
public sealed class SimulatedNodeCreator : INodeCreator
{
    private readonly ConcurrentDictionary<string, bool> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SimulatedNodeCreator> _logger;

    public SimulatedNodeCreator(ILogger<SimulatedNodeCreator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        var key = BuildKey(nodeType, path, endpoint.GetProject());
        var exists = _nodes.ContainsKey(key);
        _logger.LogDebug("[NodeStructure][Simulated] NodeExistsAsync {Key} = {Exists}.", key, exists);
        return Task.FromResult(exists);
    }

    /// <inheritdoc/>
    public Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        var key = BuildKey(nodeType, path, endpoint.GetProject());
        _nodes.TryAdd(key, true);
        _logger.LogDebug("[NodeStructure][Simulated] EnsureExistsAsync {Key}.", key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (startDate is null && finishDate is null) return Task.CompletedTask;
        _logger.LogDebug("[NodeStructure][Simulated] SetIterationDatesAsync for {Path} ({Start} – {Finish}).", path, startDate, finishDate);
        return Task.CompletedTask;
    }

    private static string BuildKey(ClassificationNodeType nodeType, string path, string project)
        => $"{nodeType}:{project}:{path}";
}
