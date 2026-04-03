#if !NETFRAMEWORK
using System;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Writes ProgressEvent records to the terminal via Console.
/// Used by the Migration Agent to render job progress in the console.
/// </summary>
public sealed class AnsiProgressSink : IProgressSink
{
    public void Emit(ProgressEvent evt)
    {
        Console.WriteLine(
            $"[{evt.Module}] {evt.Stage} WI={evt.WorkItemId} Rev={evt.RevisionsProcessed}/{evt.TotalWorkItems}{(evt.Message is not null ? $" — {evt.Message}" : string.Empty)}");
    }
}
#endif
