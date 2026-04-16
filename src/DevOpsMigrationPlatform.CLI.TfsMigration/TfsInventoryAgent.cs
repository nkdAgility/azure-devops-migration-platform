using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Extensions;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// The .NET 4.8 inventory executor.  Connects to a TFS collection, enumerates projects
/// using date-chunked WIQL queries, and emits <see cref="InventoryProgressEvent"/> records
/// as NDJSON to stdout so the parent CLI.Migration process can parse them.
/// </summary>
public sealed class TfsInventoryAgent
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Runs the inventory against the given collection.
    /// Progress is written to stdout as NDJSON; all exceptions are caught per-project and
    /// emitted as error events so the parent process can report partial results.
    /// </summary>
    public void Run(
        string collectionUrl,
        string? project,
        string? pat,
        bool allProjects,
        CancellationToken cancellationToken = default)
    {
        VssCredentials creds = string.IsNullOrEmpty(pat)
            ? new VssClientCredentials(true)
            : new VssBasicCredential(string.Empty, pat);

        using var collection = new TfsTeamProjectCollection(new Uri(collectionUrl), creds);
        collection.EnsureAuthenticated();

        var store = new WorkItemStore(collection, WorkItemStoreFlags.BypassRules);

        var projectNames = allProjects
            ? store.Projects.Cast<Project>().Select(p => p.Name).ToList()
            : new List<string> { project! };

        foreach (var projName in projectNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Create a no-op progress sink for inventory queries
            // (actual inventory progress is reported via Emit())
            var progressSink = new NoOpProgressSink();
            RunProject(collectionUrl, projName, store, progressSink, cancellationToken);
        }
    }

    private static void RunProject(
        string collectionUrl,
        string projName,
        WorkItemStore store,
        IProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        // Escape single-quotes in project name to prevent WIQL injection
        var safeName = projName.Replace("'", "''");
        var baseQuery = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{safeName}'";

        int totalWorkItems = 0;
        int totalRevisions = 0;

        try
        {
            foreach (var chunk in store.QueryCountAllByDateChunk(baseQuery, progressSink))
            {
                cancellationToken.ThrowIfCancellationRequested();

                totalWorkItems = (int)chunk.CurrentTotal;

                Emit(new InventoryProgressEvent
                {
                    ProjectName = projName,
                    Url = collectionUrl,
                    WorkItemsCount = totalWorkItems,
                    RevisionsCount = totalRevisions,
                    IsComplete = false,
                    WindowSize = chunk.CurrentChunkTimespan,
                    Timestamp = DateTime.UtcNow
                });
            }

            Emit(new InventoryProgressEvent
            {
                ProjectName = projName,
                Url = collectionUrl,
                WorkItemsCount = totalWorkItems,
                RevisionsCount = totalRevisions,
                IsComplete = true,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Emit(new InventoryProgressEvent
            {
                ProjectName = projName,
                Url = collectionUrl,
                WorkItemsCount = totalWorkItems,
                RevisionsCount = totalRevisions,
                IsComplete = true,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    private static void Emit(InventoryProgressEvent evt)
    {
        var json = JsonSerializer.Serialize(evt, JsonOpts);
        Console.WriteLine(json);
    }

    /// <summary>
    /// A no-op <see cref="IProgressSink"/> that discards all progress events.
    /// Used when progress reporting is handled through an alternative mechanism (e.g., <see cref="Emit"/>).
    /// </summary>
    private sealed class NoOpProgressSink : IProgressSink
    {
        public void Emit(ProgressEvent evt)
        {
            // No-op: actual progress is reported via TfsInventoryAgent.Emit()
        }
    }
}
