using System;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

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
        using (_logger.BeginDataScope(DataClassification.Customer))
        {
            _logger.LogInformation(
                "[{Module}] {Stage}{Message}",
                evt.Module,
                evt.Stage,
                evt.Message is not null ? $" — {evt.Message}" : string.Empty);
        }
    }
}
