using DevOpsMigrationPlatform.Abstractions.Streaming;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// IProgressSink implementation for use inside the .NET 4.8 subprocess.
/// Serialises each ProgressEvent as a single NDJSON line on stdout so that
/// the .NET 10 parent process (ExternalToolRunner) can parse and relay them.
/// All structured progress output goes through this sink — nothing is written to
/// stdout directly elsewhere in the subprocess.
/// </summary>
public sealed class StdoutProgressSink : IProgressSink
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Emit(ProgressEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, _options);
        Console.WriteLine(json);
    }
}
