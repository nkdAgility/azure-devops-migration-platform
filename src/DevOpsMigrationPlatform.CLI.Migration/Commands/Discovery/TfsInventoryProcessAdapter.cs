using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

/// <summary>
/// Spawns the <c>tfsmigration.exe inventory</c> subprocess via <see cref="ExternalToolRunner"/>
/// and converts the NDJSON stdout stream to <see cref="InventoryProgressEvent"/> records.
/// Credentials are passed as a single JSON line on stdin: <c>{"pat":"..."}</c> or <c>{}</c>
/// for Windows-integrated auth.
/// </summary>
public sealed class TfsInventoryProcessAdapter : ITfsInventoryProvider
{
    private static readonly string SubprocessPath =
        Path.Combine(AppContext.BaseDirectory, "tfsmigration.exe");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Streams inventory progress events from the TFS subprocess for a single project
    /// (or all projects when <paramref name="allProjects"/> is <c>true</c>).
    /// </summary>
    public async IAsyncEnumerable<InventoryProgressEvent> RunAsync(
        string collectionUrl,
        string? project,
        string pat,
        bool allProjects,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var argParts = new List<string> { "inventory", "--collection", QuoteArg(collectionUrl) };

        if (!string.IsNullOrWhiteSpace(project) && !allProjects)
        {
            argParts.Add("--project");
            argParts.Add(QuoteArg(project));
        }
        else if (allProjects)
        {
            argParts.Add("--all-projects");
        }

        var argString = string.Join(" ", argParts);
        var stdinPayload = JsonSerializer.Serialize(new { pat });

        var events = new ConcurrentQueue<InventoryProgressEvent>();

        var exitCode = await ExternalToolRunner.RunWithStreamingAsync(
            SubprocessPath,
            argString,
            stdinContent: stdinPayload,
            onOutput: line =>
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                try
                {
                    var evt = JsonSerializer.Deserialize<InventoryProgressEvent>(line, JsonOpts);
                    if (evt != null) events.Enqueue(evt);
                }
                catch { /* malformed line — skip */ }
            },
            cancellationToken: ct).ConfigureAwait(false);

        if (exitCode != 0 && events.IsEmpty)
        {
            events.Enqueue(new InventoryProgressEvent
            {
                ProjectName = project ?? string.Empty,
                OrgOrCollection = collectionUrl,
                IsComplete = true,
                Error = $"tfsmigration.exe exited with code {exitCode}",
                Timestamp = DateTime.UtcNow
            });
        }

        while (events.TryDequeue(out var evt))
            yield return evt;
    }

    private static string QuoteArg(string arg) =>
        arg.Contains(' ') ? $"\"{arg}\"" : arg;
}
