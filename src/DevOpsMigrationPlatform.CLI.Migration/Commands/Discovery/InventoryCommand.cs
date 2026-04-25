using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Configuration;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Rendering;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

/// <summary>
/// CLI command: discovery inventory
/// Submits a <see cref="DiscoveryJob"/> of type Inventory to the control plane.
/// In standalone mode follows progress inline with a live table; in hosted mode prints the jobId and exits.
/// </summary>
public sealed class InventoryCommand : ControlPlaneCommandBase<InventoryCommand.Settings>
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
            services.AddOptions<DiscoveryOptions>().Bind(config.GetSection("MigrationPlatform"));
            services.AddDiscoveryOptionsOrganisationsBinder();
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
            DiscoveryType = DiscoveryJobType.Inventory,
            Organisations = discoveryOpts.Organisations
                .Where(o => o.Enabled)
                .Select(o => new ScopedOrganisationEndpoint
                {
                    Endpoint = o.ToEndpointOptions(),
                    Projects = new List<string>(o.Projects),
                    Scopes = o.Scopes.Select(s => new JobModuleScope
                    {
                        Type = s.Type,
                        Parameters = s.Parameters.ToDictionary(
                            kvp => kvp.Key,
                            kvp => (object?)kvp.Value)
                    }).ToList()
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

        console.MarkupLine($"[blue]ℹ[/] Submitting inventory job for [bold]{job.Organisations.Count}[/] organisation(s).");
        console.MarkupLine($"[blue]ℹ[/] Output path: [blue]{Markup.Escape(outputPath)}[/]");

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

        // Pre-populate the table with known projects from config so the user gets
        // immediate visual feedback while the agent acquires a lease and starts processing.
        var summaries = new Dictionary<string, InventorySummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var org in job.Organisations)
        {
            var url = org.Endpoint.GetResolvedUrl();
            foreach (var project in org.Projects)
            {
                var key = $"{url}|{project}";
                if (!summaries.ContainsKey(key))
                    summaries[key] = new InventorySummary { Url = url, ProjectName = project };
            }
        }

        // Status message shown below the table during long resume skips.
        string statusMessage = "";
        var startTime = DateTimeOffset.UtcNow;
        DateTimeOffset? latestCheckpointAt = null;
        DateTimeOffset? nextCheckpointDueAt = null;

        try
        {
            if (console.Profile.Capabilities.Interactive)
            {
                var table = BuildTable(summaries.Values);
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
                            if (evt.Stage == "Completed")
                                break;

                            UpdateSummaryFromProgress(summaries, evt);

                            // Show activity messages (e.g. "Skipping completed project…")
                            // even when the event has no LastProcessed key.
                            if (!string.IsNullOrEmpty(evt.Message))
                                statusMessage = evt.Message;

                            latestCheckpointAt = evt.LastCheckpointAt ?? latestCheckpointAt;
                            nextCheckpointDueAt = evt.NextCheckpointDueAt;
                            ctx.UpdateTarget(BuildLivePanel(summaries.Values, statusMessage, startTime, latestCheckpointAt, nextCheckpointDueAt));
                        }
                    });
            }
            else
            {
                await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                {
                    if (evt.Stage == "Completed")
                        break;

                    UpdateSummaryFromProgress(summaries, evt);
                    var key = evt.Message ?? "";

                    if (summaries.TryGetValue(key, out var s))
                    {
                        var status = s.Error != null ? "✗ Failed" : "✓";
                        console.MarkupLine(
                            $"  {Markup.Escape(s.Url)} / {Markup.Escape(s.ProjectName)}: " +
                            $"{s.WorkItemsCount} work items, {s.RevisionsCount} revisions — {status}");
                    }
                    else if (!string.IsNullOrEmpty(evt.Message))
                    {
                        // Non-table events (e.g. "Skipping completed project…")
                        console.MarkupLine($"[grey]  {Markup.Escape(evt.Message)}[/]");
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

        console.MarkupLine($"\n[green]✅ Inventory complete.[/] Results written to [blue]{Markup.Escape(outputPath)}[/]");

        // ── Post-processing: fan out per-org/per-project inventory files ──────
        FanOutInventoryFiles(outputPath, console);

        return 0;
    }

    // ── Progress-to-summary mapping ───────────────────────────────────────────

    private static void UpdateSummaryFromProgress(
        Dictionary<string, InventorySummary> summaries,
        ProgressEvent evt)
    {
        // Message now carries the "{url}|{projectName}" key that was formerly in LastProcessed.
        var key = evt.Message;
        if (string.IsNullOrEmpty(key))
            return;

        var separatorIndex = key.IndexOf('|');
        if (separatorIndex < 0)
            return;

        var url = key[..separatorIndex];
        var project = key[(separatorIndex + 1)..];

        if (!summaries.TryGetValue(key, out var summary))
        {
            summary = new InventorySummary { Url = url, ProjectName = project };
            summaries[key] = summary;
        }

        var inv = evt.Metrics?.Discovery?.Inventory;
        summary.WorkItemsCount = evt.Metrics?.Scope?.WorkItemsTotal ?? summary.WorkItemsCount;
        summary.RevisionsCount = inv?.RevisionsTotal ?? summary.RevisionsCount;
        summary.ReposCount = inv?.RepositoriesTotal ?? summary.ReposCount;
        summary.IsComplete = evt.Stage != "Progress";
        summary.LastUpdatedUtc = evt.Timestamp.UtcDateTime;

        if (evt.Stage == "Failed")
            summary.Error = "Project inventory failed";
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private static Table BuildTable(IEnumerable<InventorySummary> summaries)
    {
        var table = new Table()
            .Title("[bold yellow]Discovery Inventory[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Organisation / Collection")
            .AddColumn("Project")
            .AddColumn(new TableColumn("Work Items").RightAligned())
            .AddColumn(new TableColumn("Revisions").RightAligned())
            .AddColumn(new TableColumn("Repos").RightAligned())
            .AddColumn("Status");

        foreach (var s in summaries)
        {
            var status = s.Error != null
                ? "[red]✗ Failed[/]"
                : s.IsComplete
                    ? "[green]✓[/]"
                    : "[grey]…[/]";

            var partial = s.Error != null && (s.WorkItemsCount > 0 || s.RevisionsCount > 0);
            var wiCount = partial ? $"~{s.WorkItemsCount}" : s.WorkItemsCount.ToString();
            var revCount = partial ? $"~{s.RevisionsCount}" : s.RevisionsCount.ToString();

            table.AddRow(
                Markup.Escape(s.Url),
                Markup.Escape(s.ProjectName),
                wiCount,
                revCount,
                s.ReposCount.ToString(),
                status);
        }

        return table;
    }

    /// <summary>
    /// Builds the live display content: the inventory table plus a status line showing
    /// current activity (e.g. "Skipping completed project…" during resume).
    /// </summary>
    private static IRenderable BuildLivePanel(IEnumerable<InventorySummary> summaries, string statusMessage, DateTimeOffset startTime, DateTimeOffset? lastCheckpointAt = null, DateTimeOffset? nextCheckpointDueAt = null)
    {
        var table = BuildTable(summaries);
        var parts = new List<IRenderable> { table };

        // Throughput stats panel
        var stats = BuildThroughputPanel(summaries, startTime, lastCheckpointAt, nextCheckpointDueAt);
        if (stats is not null)
            parts.Add(stats);

        if (!string.IsNullOrEmpty(statusMessage))
            parts.Add(new Markup($"[grey]{Markup.Escape(statusMessage)}[/]"));

        return parts.Count == 1 ? table : new Rows(parts);
    }

    /// <summary>
    /// Builds a throughput stats panel showing rates, elapsed time, and ETA.
    /// Returns null if no meaningful data is available yet.
    /// </summary>
    private static IRenderable? BuildThroughputPanel(IEnumerable<InventorySummary> summaries, DateTimeOffset startTime, DateTimeOffset? lastCheckpointAt = null, DateTimeOffset? nextCheckpointDueAt = null)
    {
        var elapsed = DateTimeOffset.UtcNow - startTime;
        if (elapsed.TotalSeconds < 1)
            return null;

        int total = 0, completed = 0, failed = 0;
        long totalWi = 0, totalRev = 0;
        foreach (var s in summaries)
        {
            total++;
            if (s.Error != null) failed++;
            else if (s.IsComplete) completed++;
            totalWi += s.WorkItemsCount;
            totalRev += s.RevisionsCount;
        }

        var remaining = total - completed - failed;
        var hours = elapsed.TotalHours;

        var statsTable = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Label").NoWrap())
            .AddColumn(new TableColumn("Value").RightAligned());

        statsTable.AddRow("[dim]Elapsed[/]", $"[white]{FormatTimeSpan(elapsed)}[/]");

        if (totalWi > 0 || totalRev > 0)
        {
            var wiPerHour = hours > 0.001 ? totalWi / hours : 0;
            var revPerHour = hours > 0.001 ? totalRev / hours : 0;
            statsTable.AddRow("[dim]Work Items / hour[/]", $"[white]{wiPerHour:N0}[/]");
            statsTable.AddRow("[dim]Revisions / hour[/]", $"[white]{revPerHour:N0}[/]");
        }

        if (completed > 0)
        {
            var projPerHour = hours > 0.001 ? completed / hours : 0;
            statsTable.AddRow("[dim]Projects / hour[/]", $"[white]{projPerHour:N1}[/]");

            var avgMs = elapsed.TotalMilliseconds / completed;
            statsTable.AddRow("[dim]Avg Project Duration[/]", $"[white]{FormatTimeSpan(TimeSpan.FromMilliseconds(avgMs))}[/]");

            if (remaining > 0)
            {
                var eta = TimeSpan.FromMilliseconds(avgMs * remaining);
                statsTable.AddRow("[dim]ETA (remaining)[/]", $"[yellow]{FormatTimeSpan(eta)}[/]");
            }
        }

        // Checkpoint safety indicator
        if (nextCheckpointDueAt is null && lastCheckpointAt is not null)
        {
            statsTable.AddRow("[dim]Checkpoint[/]", "[green]✓ Safe to cancel (per-item)[/]");
        }
        else if (nextCheckpointDueAt is not null)
        {
            var cpRemaining = nextCheckpointDueAt.Value - DateTimeOffset.UtcNow;
            if (cpRemaining <= TimeSpan.Zero)
                statsTable.AddRow("[dim]Checkpoint[/]", "[green]✓ Save point due now[/]");
            else
                statsTable.AddRow("[dim]Checkpoint[/]", $"[yellow]⏳ Next save in {FormatTimeSpan(cpRemaining)}[/]");
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

    // ── Per-org/per-project fan-out ───────────────────────────────────────────

    /// <summary>
    /// Reads <c>inventory.json</c> from the output root and writes per-org and
    /// per-project <c>inventory.json</c> / <c>inventory.csv</c> files into the
    /// <c>&lt;org&gt;/&lt;project&gt;/</c> folder hierarchy.
    /// </summary>
    internal static void FanOutInventoryFiles(string outputPath, IAnsiConsole console)
    {
        var jsonPath = Path.Combine(outputPath, "inventory.json");
        if (!File.Exists(jsonPath))
            return;

        InventoryReport? report;
        try
        {
            var json = File.ReadAllText(jsonPath);
            report = JsonSerializer.Deserialize<InventoryReport>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch
        {
            return; // non-fatal — root files are still valid
        }

        if (report?.Organisations is null)
            return;

        var jsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        foreach (var org in report.Organisations)
        {
            var orgFolderName = SanitiseFolderName(org.Url);
            var orgDir = Path.Combine(outputPath, orgFolderName);
            Directory.CreateDirectory(orgDir);

            // Org-level inventory.json
            var orgReport = new InventoryReport
            {
                GeneratedAt = report.GeneratedAt,
                Totals = org.Totals,
                Organisations = new[] { org }
            };
            File.WriteAllText(Path.Combine(orgDir, "inventory.json"),
                JsonSerializer.Serialize(orgReport, jsonOpts));

            // Org-level inventory.csv
            WriteCsv(Path.Combine(orgDir, "inventory.csv"), org.Projects);

            foreach (var proj in org.Projects)
            {
                var projDir = Path.Combine(orgDir, proj.Name);
                Directory.CreateDirectory(projDir);

                // Project-level inventory.json
                var projOrg = new OrganisationInventory
                {
                    Url = org.Url,
                    Totals = new InventoryTotals
                    {
                        WorkItems = proj.WorkItems,
                        Revisions = proj.Revisions,
                        Repos = proj.Repos,
                        Projects = 1
                    },
                    Projects = new[] { proj }
                };
                var projReport = new InventoryReport
                {
                    GeneratedAt = report.GeneratedAt,
                    Totals = projOrg.Totals,
                    Organisations = new[] { projOrg }
                };
                File.WriteAllText(Path.Combine(projDir, "inventory.json"),
                    JsonSerializer.Serialize(projReport, jsonOpts));

                // Project-level inventory.csv
                WriteCsv(Path.Combine(projDir, "inventory.csv"), new[] { proj });
            }
        }

        console.MarkupLine($"[green]✓[/] Per-org/per-project inventory files written to [blue]{Markup.Escape(outputPath)}[/]");
    }

    private static void WriteCsv(string path, IEnumerable<ProjectInventory> projects)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(false));
        w.WriteLine("ProjectName,WorkItems,Revisions,Repos,IsComplete,Error");
        foreach (var p in projects)
            w.WriteLine($"{CsvEscape(p.Name)},{p.WorkItems},{p.Revisions},{p.Repos},{p.IsComplete},{CsvEscape(p.Error ?? "")}");
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static string SanitiseFolderName(string url) =>
        CliPathUtilities.ExtractOrgFolderName(url);
}
