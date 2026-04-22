using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Migration.Utilities;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

/// <summary>
/// CLI command: discovery dependencies
/// Submits a <see cref="DiscoveryJob"/> of type Dependencies to the control plane.
/// In standalone mode follows progress inline with a live table; in hosted mode prints the jobId and exits.
/// After job completion reads the root dependencies.csv from the package and generates per-project/per-org
/// output (grouped CSVs, Mermaid diagrams) plus console summary tables.
/// </summary>
public sealed class DependencyCommand : ControlPlaneCommandBase<DependencyCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings, IRequiresMigrationConfig
    {
        [CommandOption("-o|--output")]
        [Description("Override the output directory for discovery results (overrides Package.WorkingDirectory in the config).")]
        public string? OutputDirectory { get; set; }
    }

    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });
            services.AddAzureDevOpsDependencyAnalysis(config);
        });

        var console = GetRequiredService<IAnsiConsole>();
        var discoveryOpts = GetRequiredService<IOptions<DiscoveryOptions>>().Value;

        try { discoveryOpts.Validate(); }
        catch (InvalidOperationException ex)
        {
            ShowError(console, ex.Message);
            return 1;
        }

        var outputPath = string.IsNullOrWhiteSpace(settings.OutputDirectory)
            ? Path.GetFullPath(discoveryOpts.Package.ExpandedPath)
            : Path.GetFullPath(settings.OutputDirectory);
        var packageUri = $"file:///{outputPath.Replace(Path.DirectorySeparatorChar, '/')}";

        var job = new DiscoveryJob
        {
            JobId = Guid.NewGuid().ToString(),
            ConfigVersion = "1.0",
            DiscoveryType = DiscoveryJobType.Dependencies,
            Organisations = discoveryOpts.Organisations
                .Where(o => o.Enabled)
                .Select(o => new ScopedOrganisationEndpoint
                {
                    Endpoint = o.ToEndpointOptions(),
                    Projects = new List<string>(o.Projects)
                }).ToList(),
            Policies = new JobPolicies
            {
                MaxRetries = discoveryOpts.Policies.Retries.Max,
                MaxConcurrency = discoveryOpts.Policies.Throttle.MaxConcurrency,
                CheckpointIntervalSeconds = discoveryOpts.Policies.Checkpoints.Interval
            },
            Package = new JobPackage { PackageUri = packageUri }
        };

        var envOpts = GetRequiredService<IOptions<EnvironmentOptions>>().Value;
        var isStandalone = envOpts.Type == EnvironmentType.Standalone;
        var client = GetRequiredService<ControlPlaneClient>();

        console.MarkupLine($"[blue]ℹ[/] Submitting dependency discovery job for [bold]{job.Organisations.Count}[/] organisation(s).");
        console.MarkupLine($"[blue]ℹ[/] Output path: [blue]{Markup.Escape(outputPath)}[/]");

        // ── Pre-load inventory.json for grand totals display ─────────────────
        InventoryReport? inventoryReport = null;
        var inventoryJsonPath = Path.Combine(outputPath, "inventory.json");
        if (File.Exists(inventoryJsonPath))
        {
            try
            {
                var inventoryJsonText = File.ReadAllText(inventoryJsonPath);
                inventoryReport = JsonSerializer.Deserialize<InventoryReport>(inventoryJsonText,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                if (inventoryReport is not null)
                    console.MarkupLine($"[blue]ℹ[/] Inventory loaded: [bold]{inventoryReport.Totals.WorkItems:N0}[/] work items across [bold]{inventoryReport.Totals.Projects}[/] projects.");
            }
            catch
            {
                // Non-fatal — proceed without pre-counts.
            }
        }

        var controlPlaneUrl = GetControlPlaneUrl();

        Guid jobId;
        try
        {
            jobId = await client.SubmitDiscoveryAsync(job, cancellationToken);
            PrintJobSubmitted(console, jobId, controlPlaneUrl);
        }
        catch (Exception ex)
        {
            ShowError(console, $"Failed to submit discovery job: {ex.Message}");
            return 1;
        }

        if (!isStandalone)
        {
            console.MarkupLine($"[grey]Use [blue]manage status --job {jobId}[/] to check progress.[/]");
            return 0;
        }

        // Pre-populate the progress table with known projects from config so the user gets
        // immediate visual feedback while the agent acquires a lease and starts processing.
        var progressState = new Dictionary<string, ProjectProgress>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in job.Organisations)
        {
            var url = org.Endpoint.GetResolvedUrl();
            foreach (var project in org.Projects)
            {
                var key = $"{url}|{project}";
                if (!progressState.ContainsKey(key))
                    progressState[key] = new ProjectProgress { OrgName = url, ProjectName = project };
            }
        }

        var startTime = DateTimeOffset.UtcNow;
        string currentPhase = "Dependencies"; // tracks which module is running
        DateTimeOffset? latestCheckpointAt = null; // updated from progress events
        DateTimeOffset? nextCheckpointDueAt = null; // updated from progress events

        try
        {
            if (console.Profile.Capabilities.Interactive)
            {
                var table = BuildProgressTable(progressState.Values);
                await console.Live(table)
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .StartAsync(async ctx =>
                    {
                        // Spectre.Console Live does NOT render until the callback
                        // calls Refresh/UpdateTarget. Force the initial render so
                        // the pre-populated table is visible immediately.
                        ctx.Refresh();

                        await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                        {
                            // Only break when the Dependencies module (the final module) completes.
                            if (evt.Stage == "Completed" && evt.Module == "Dependencies")
                                break;

                            // Handle inventory events when the agent runs inventory as a prerequisite.
                            if (evt.Module == "Inventory")
                            {
                                if (currentPhase != "Inventory")
                                {
                                    currentPhase = "Inventory";
                                    ctx.UpdateTarget(new Markup(
                                        "[yellow]⚠ No inventory found — running inventory discovery first…[/]\n"));
                                }

                                if (evt.Stage == "Completed")
                                {
                                    currentPhase = "Dependencies";
                                    // Re-load inventory.json now that it exists.
                                    if (File.Exists(inventoryJsonPath))
                                    {
                                        try
                                        {
                                            var inventoryJsonText = File.ReadAllText(inventoryJsonPath);
                                            inventoryReport = JsonSerializer.Deserialize<InventoryReport>(inventoryJsonText,
                                                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                                        }
                                        catch { /* non-fatal */ }
                                    }
                                    ctx.UpdateTarget(new Rows(
                                        new Markup("[green]✓ Inventory complete.[/] Starting dependency analysis…\n"),
                                        BuildLivePanel(progressState.Values, startTime, latestCheckpointAt, nextCheckpointDueAt)));
                                    continue;
                                }

                                // Show inventory progress as a status line.
                                var invMsg = evt.Message ?? "Counting work items…";
                                ctx.UpdateTarget(new Markup(
                                    $"[yellow]Inventory:[/] [grey]{Markup.Escape(invMsg)}[/]\n"));
                                continue;
                            }

                            UpdateProgressFromEvent(progressState, evt);
                            latestCheckpointAt = evt.LastCheckpointAt ?? latestCheckpointAt;
                            nextCheckpointDueAt = evt.NextCheckpointDueAt;
                            ctx.UpdateTarget(BuildLivePanel(progressState.Values, startTime, latestCheckpointAt, nextCheckpointDueAt));
                        }
                    });
            }
            else
            {
                await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                {
                    // Only break when the Dependencies module (the final module) completes.
                    if (evt.Stage == "Completed" && evt.Module == "Dependencies")
                        break;

                    // Handle inventory events when the agent runs inventory as a prerequisite.
                    if (evt.Module == "Inventory")
                    {
                        if (currentPhase != "Inventory")
                        {
                            currentPhase = "Inventory";
                            console.MarkupLine("[yellow]⚠ No inventory found — running inventory discovery first…[/]");
                        }

                        if (evt.Stage == "Completed")
                        {
                            currentPhase = "Dependencies";
                            // Re-load inventory.json now that it exists.
                            if (File.Exists(inventoryJsonPath))
                            {
                                try
                                {
                                    var inventoryJsonText = File.ReadAllText(inventoryJsonPath);
                                    inventoryReport = JsonSerializer.Deserialize<InventoryReport>(inventoryJsonText,
                                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                                }
                                catch { /* non-fatal */ }
                            }
                            console.MarkupLine("[green]✓ Inventory complete.[/] Starting dependency analysis…");
                            continue;
                        }

                        if (!string.IsNullOrEmpty(evt.Message))
                            console.MarkupLine($"[grey]  {Markup.Escape(evt.Message)}[/]");
                        continue;
                    }

                    UpdateProgressFromEvent(progressState, evt);

                    if (evt.Stage == "ProjectComplete")
                    {
                        var key = evt.LastProcessed ?? "";
                        if (progressState.TryGetValue(key, out var p))
                        {
                            console.MarkupLine(
                                $"  [grey]{Markup.Escape(p.OrgName)}[/] / [white]{Markup.Escape(p.ProjectName)}[/]: " +
                                $"{p.WorkItemsAnalysed} items analysed, " +
                                $"{p.CrossProjectLinks} cross-project, " +
                                $"[red]{p.CrossOrgLinks}[/] cross-org — [green]✓[/]");
                        }
                    }
                }
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("failed"))
        {
            ShowError(console, "Discovery job failed. Check agent logs for details.");
            return 1;
        }
        catch (OperationCanceledException)
        {
            if (!isStandalone)
            {
                console.MarkupLine("[yellow]Detached from stream. Discovery job continues running.[/]");
                return 0;
            }
            throw; // Standalone: propagate so base class shows "Operation cancelled" and disposes LocalStackHost
        }

        // ── Console summary from progress events ─────────────────────────
        // All file output (per-project CSV, per-org CSV, grouped CSV, Mermaid diagrams,
        // transitive analysis) is now generated by the DependencyDiscoveryModule via
        // IArtefactStore. The CLI only displays a summary to the console.
        var totalAnalysed = progressState.Values.Sum(p => p.WorkItemsAnalysed);
        var totalCrossProject = progressState.Values.Sum(p => p.CrossProjectLinks);
        var totalCrossOrg = progressState.Values.Sum(p => p.CrossOrgLinks);
        var totalLinks = totalCrossProject + totalCrossOrg;

        console.WriteLine();
        var summaryTable = new Table()
            .Title("[bold]Discovery Summary[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Metric")
            .AddColumn(new TableColumn("Count").RightAligned());
        summaryTable.AddRow("Work Items Analysed", totalAnalysed.ToString("N0"));
        summaryTable.AddRow("Total External Links", totalLinks.ToString("N0"));
        summaryTable.AddRow("Cross-Project Links", totalCrossProject.ToString("N0"));
        summaryTable.AddRow("[red]⚠ Cross-Organisation Links[/]", $"[red]{totalCrossOrg}[/]");
        summaryTable.AddRow("Output Directory", Markup.Escape(outputPath));
        console.Write(summaryTable);

        if (totalCrossOrg > 0)
            console.MarkupLine($"[red]⚠ ACTION REQUIRED: {totalCrossOrg} cross-organisation link(s) will break after migration[/]");

        console.MarkupLine($"\n[green]✓[/] Dependency discovery completed successfully.");
        console.MarkupLine($"[green]✓[/] Results written to package at [blue]{Markup.Escape(outputPath)}[/]");
        return 0;
    }

    // ── Progress tracking ─────────────────────────────────────────────────────

    private static void UpdateProgressFromEvent(
        Dictionary<string, ProjectProgress> state,
        ProgressEvent evt)
    {
        var key = evt.LastProcessed;
        if (string.IsNullOrEmpty(key))
            return;

        var separatorIndex = key.IndexOf('|');
        if (separatorIndex < 0)
            return;

        var orgUrl = key[..separatorIndex];
        var project = key[(separatorIndex + 1)..];
        var orgName = ExtractOrgName(orgUrl);

        if (!state.TryGetValue(key, out var progress))
        {
            progress = new ProjectProgress { OrgName = orgName, ProjectName = project };
            state[key] = progress;
        }

        progress.TotalWorkItems = evt.TotalWorkItems;
        progress.WorkItemsAnalysed = evt.WorkItemsProcessed;
        progress.ExternalLinks = evt.WorkItemId;
        progress.CrossProjectLinks = evt.RevisionsProcessed;
        progress.CrossOrgLinks = evt.AttachmentsProcessed;
        progress.IsComplete = evt.Stage == "ProjectComplete";

        if (evt.Stage == "Failed")
            progress.Error = evt.Message;
    }

    // ── Live table rendering ──────────────────────────────────────────────────

    private static Table BuildProgressTable(IEnumerable<ProjectProgress> state)
    {
        var table = new Table()
            .Title("[bold yellow]Dependency Discovery[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Organisation")
            .AddColumn("Project")
            .AddColumn(new TableColumn("Analysed").RightAligned())
            .AddColumn(new TableColumn("Links").RightAligned())
            .AddColumn(new TableColumn("Cross-Project").RightAligned())
            .AddColumn(new TableColumn("Cross-Org").RightAligned())
            .AddColumn("Status");

        foreach (var p in state)
        {
            var status = p.Error is not null
                ? "[red]✗ Failed[/]"
                : p.IsComplete
                    ? "[green]✓[/]"
                    : "[grey]…[/]";
            var crossOrg = p.CrossOrgLinks > 0
                ? $"[red]{p.CrossOrgLinks}[/]"
                : p.CrossOrgLinks.ToString();
            var analysed = p.TotalWorkItems > 0
                ? $"{p.WorkItemsAnalysed:N0}/{p.TotalWorkItems:N0}"
                : p.WorkItemsAnalysed.ToString("N0");
            table.AddRow(
                Markup.Escape(p.OrgName),
                Markup.Escape(p.ProjectName),
                analysed,
                p.ExternalLinks.ToString("N0"),
                p.CrossProjectLinks.ToString("N0"),
                crossOrg,
                status);
        }

        return table;
    }

    /// <summary>
    /// Builds the live display content: the progress table plus a throughput stats panel.
    /// </summary>
    private static IRenderable BuildLivePanel(IEnumerable<ProjectProgress> state, DateTimeOffset startTime, DateTimeOffset? lastCheckpointAt = null, DateTimeOffset? nextCheckpointDueAt = null)
    {
        var table = BuildProgressTable(state);
        var stats = BuildThroughputPanel(state, startTime, lastCheckpointAt, nextCheckpointDueAt);
        return stats is not null ? new Rows(table, stats) : table;
    }

    /// <summary>
    /// Builds a throughput stats panel showing rates, elapsed time, and ETA.
    /// </summary>
    private static IRenderable? BuildThroughputPanel(IEnumerable<ProjectProgress> state, DateTimeOffset startTime, DateTimeOffset? lastCheckpointAt = null, DateTimeOffset? nextCheckpointDueAt = null)
    {
        var elapsed = DateTimeOffset.UtcNow - startTime;
        if (elapsed.TotalSeconds < 1)
            return null;

        int completed = 0, inProgress = 0;
        long totalAnalysed = 0, totalKnown = 0, processedOfKnown = 0;
        foreach (var p in state)
        {
            if (p.IsComplete) completed++;
            else if (p.WorkItemsAnalysed > 0) inProgress++;
            totalAnalysed += p.WorkItemsAnalysed;
            if (p.TotalWorkItems > 0)
            {
                totalKnown += p.TotalWorkItems;
                processedOfKnown += p.IsComplete ? p.TotalWorkItems : p.WorkItemsAnalysed;
            }
        }

        var hours = elapsed.TotalHours;

        var statsTable = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Label").NoWrap())
            .AddColumn(new TableColumn("Value").RightAligned());

        statsTable.AddRow("[dim]Elapsed[/]", $"[white]{FormatTimeSpan(elapsed)}[/]");

        if (totalAnalysed > 0)
        {
            var analysedPerHour = hours > 0.001 ? totalAnalysed / hours : 0;
            statsTable.AddRow("[dim]Work Items Analysed / hour[/]", $"[white]{analysedPerHour:N0}[/]");
        }

        if (completed > 0)
        {
            var projPerHour = hours > 0.001 ? completed / hours : 0;
            statsTable.AddRow("[dim]Projects / hour[/]", $"[white]{projPerHour:N1}[/]");

            var avgMs = elapsed.TotalMilliseconds / completed;
            statsTable.AddRow("[dim]Avg Project Duration[/]", $"[white]{FormatTimeSpan(TimeSpan.FromMilliseconds(avgMs))}[/]");
        }

        // ETA: prefer per-item rate when we have pre-count totals
        if (totalKnown > 0 && processedOfKnown > 0 && processedOfKnown < totalKnown)
        {
            var msPerItem = elapsed.TotalMilliseconds / processedOfKnown;
            var remaining = totalKnown - processedOfKnown;
            var eta = TimeSpan.FromMilliseconds(msPerItem * remaining);
            statsTable.AddRow("[dim]ETA (remaining)[/]", $"[yellow]{FormatTimeSpan(eta)}[/]");
        }
        else if (completed > 0 && inProgress > 0)
        {
            var avgMs = elapsed.TotalMilliseconds / completed;
            var eta = TimeSpan.FromMilliseconds(avgMs * inProgress);
            statsTable.AddRow("[dim]ETA (remaining)[/]", $"[yellow]{FormatTimeSpan(eta)}[/]");
        }

        // Checkpoint safety indicator
        if (nextCheckpointDueAt is null && lastCheckpointAt is not null)
        {
            statsTable.AddRow("[dim]Checkpoint[/]", "[green]✓ Safe to cancel (per-item)[/]");
        }
        else if (nextCheckpointDueAt is not null)
        {
            var remaining = nextCheckpointDueAt.Value - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
                statsTable.AddRow("[dim]Checkpoint[/]", "[green]✓ Save point due now[/]");
            else
                statsTable.AddRow("[dim]Checkpoint[/]", $"[yellow]⏳ Next save in {FormatTimeSpan(remaining)}[/]");
        }

        return new Panel(statsTable)
            .Header("[bold yellow]Throughput[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .Expand();
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        if (ts.TotalMinutes >= 1)
            return $"{(int)ts.TotalMinutes}m {ts.Seconds:D2}s";
        return $"{ts.Seconds}s";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExtractOrgName(string orgUrl) =>
        PathUtilities.ExtractOrgFolderName(orgUrl);

    private sealed class ProjectProgress
    {
        public string OrgName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int TotalWorkItems { get; set; }
        public int WorkItemsAnalysed { get; set; }
        public int ExternalLinks { get; set; }
        public int CrossProjectLinks { get; set; }
        public int CrossOrgLinks { get; set; }
        public bool IsComplete { get; set; }
        public string? Error { get; set; }
    }
}
