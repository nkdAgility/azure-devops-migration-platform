#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Writes ProgressEvent records to the structured log pipeline.
/// Used by the Migration Agent to render job progress.
/// </summary>
public sealed class AnsiProgressSink : IProgressSink
{
    private readonly ILogger<AnsiProgressSink> _logger;

    public AnsiProgressSink(ILogger<AnsiProgressSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Emit(ProgressEvent evt)
    {
        _logger.LogInformation(
            "[{Module}] {Stage} WI={WorkItemId} Rev={RevisionsProcessed}/{TotalWorkItems}{Message}",
            evt.Module,
            evt.Stage,
            evt.WorkItemId,
            evt.RevisionsProcessed,
            evt.TotalWorkItems,
            evt.Message is not null ? $" — {evt.Message}" : string.Empty);
    }
}
#endif
