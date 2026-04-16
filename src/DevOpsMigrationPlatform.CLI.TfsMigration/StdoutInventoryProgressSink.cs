using System;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// Serialises each <see cref="InventoryProgressEvent"/> as a single NDJSON line
/// on stdout so the parent CLI.Migration process can parse them.
/// Mirrors <see cref="StdoutProgressSink"/> but for inventory-specific events.
/// </summary>
internal sealed class StdoutInventoryProgressSink
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public void Emit(InventoryProgressEvent evt)
    {
        if (evt == null) throw new ArgumentNullException(nameof(evt));
        var json = JsonSerializer.Serialize(evt, JsonOpts);
        Console.WriteLine(json);
    }
}
