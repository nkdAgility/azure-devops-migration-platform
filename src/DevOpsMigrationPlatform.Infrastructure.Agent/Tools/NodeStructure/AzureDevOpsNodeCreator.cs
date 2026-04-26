#if !NET481
using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Placeholder implementation of <see cref="INodeCreator"/> for the ADO REST connector.
/// Concrete ADO connector implementations register a subclass that makes actual HTTP calls.
/// </summary>
public class AzureDevOpsNodeCreator : INodeCreator
{
    private readonly ILogger<AzureDevOpsNodeCreator> _logger;

    public AzureDevOpsNodeCreator(ILogger<AzureDevOpsNodeCreator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public virtual Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
    {
        _logger.LogDebug("[NodeStructure] NodeExistsAsync called for {Type} {Path}", nodeType, path);
        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public virtual Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
    {
        _logger.LogDebug("[NodeStructure] EnsureExistsAsync called for {Type} {Path}", nodeType, path);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual Task SetIterationDatesAsync(string path, DateTimeOffset? startDate, DateTimeOffset? finishDate, CancellationToken ct)
    {
        if (startDate is null && finishDate is null) return Task.CompletedTask;
        _logger.LogDebug("[NodeStructure] SetIterationDatesAsync called for {Path}", path);
        return Task.CompletedTask;
    }
}
#endif
