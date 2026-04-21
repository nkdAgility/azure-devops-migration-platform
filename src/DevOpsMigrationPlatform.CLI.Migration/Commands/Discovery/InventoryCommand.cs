using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

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
            services.AddAzureDevOpsInventory(config);
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
                        await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                        {
                            if (evt.Stage == "Completed")
                                break;

                            UpdateSummaryFromProgress(summaries, evt);
                            ctx.UpdateTarget(BuildTable(summaries.Values));
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
                    var key = evt.LastProcessed ?? evt.Message ?? "";
                    if (summaries.TryGetValue(key, out var s))
                    {
                        var status = s.Error != null ? "✗ Failed" : "✓";
                        console.MarkupLine(
                            $"  {Markup.Escape(s.Url)} / {Markup.Escape(s.ProjectName)}: " +
                            $"{s.WorkItemsCount} work items, {s.RevisionsCount} revisions — {status}");
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
        return 0;
    }

    // ── Progress-to-summary mapping ───────────────────────────────────────────

    private static void UpdateSummaryFromProgress(
        Dictionary<string, InventorySummary> summaries,
        ProgressEvent evt)
    {
        // LastProcessed carries "{url}|{projectName}" from InventoryDiscoveryModule.
        var key = evt.LastProcessed;
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

        summary.WorkItemsCount = evt.TotalWorkItems;
        summary.RevisionsCount = evt.RevisionsProcessed;
        summary.ReposCount = evt.AttachmentsProcessed;
        summary.IsComplete = evt.Stage != "Progress";
        summary.LastUpdatedUtc = evt.Timestamp.UtcDateTime;

        if (evt.Stage == "Failed")
            summary.Error = evt.Message;
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
}
