using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Utilities;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using OpenTelemetry.Trace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

public sealed class InventoryCommand : CommandBase<InventoryCommand.Settings>
{
    private readonly IInventoryService _inventoryService;
    private readonly TfsInventoryProcessAdapter _tfsAdapter;
    private readonly IOptions<InventoryOptions> _options;

    public InventoryCommand(
        IServiceProvider serviceProvider,
        IHostApplicationLifetime applicationLifetime,
        ILogger<InventoryCommand> logger,
        ActivitySource activitySource,
        IInventoryService inventoryService,
        TfsInventoryProcessAdapter tfsAdapter,
        IOptions<InventoryOptions> options) 
        : base(serviceProvider, applicationLifetime, logger, activitySource)
    {
        _inventoryService = inventoryService;
        _tfsAdapter = tfsAdapter;
        _options = options;
    }

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--all-projects")]
        [Description("Inventory all projects in the organisation (Mode 1 only; ignored in Mode 2)")]
        public bool AllProjects { get; set; }

        [CommandOption("--output <PATH>")]
        [Description("Directory where discovery-summary.csv is written (default: current working directory)")]
        public string? OutputPath { get; set; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        return await RunCoreAsync(settings, cancellationToken);
    }

    private async Task<int> RunCoreAsync(Settings settings, CancellationToken ct)
    {
        var opts = _options.Value;
        opts.Validate(settings.AllProjects);

        var summaries = new Dictionary<string, InventorySummary>(StringComparer.OrdinalIgnoreCase);

        if (opts.Source != null)
            await RunMode1Async(opts.Source, settings.AllProjects, summaries, ct);
        else
            await RunMode2Async(opts.Organisations!, summaries, ct);

        var outputDir = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? Directory.GetCurrentDirectory()
            : settings.OutputPath;

        var csvPath = Path.Combine(outputDir, "discovery-summary.csv");
        WriteCsv(summaries.Values, csvPath);

        AnsiConsole.MarkupLine($"\n[green]✅ Inventory complete.[/] CSV written to [blue]{Markup.Escape(csvPath)}[/]");
        return 0;
    }

    // ── Mode 1: source-based ──────────────────────────────────────────────────

    private async Task RunMode1Async(
        MigrationEndpointOptions source,
        bool allProjects,
        Dictionary<string, InventorySummary> summaries,
        CancellationToken ct)
    {
        var pat = TokenResolver.Resolve(source.Authentication?.AccessToken) ?? string.Empty;

        var isTfs = string.Equals(source.Type, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase);

        List<string> projects;
        if (!string.IsNullOrWhiteSpace(source.Project))
            projects = new List<string> { source.Project };
        else if (isTfs)
            projects = new List<string>();  // TfsInventoryProcessAdapter uses --all-projects
        else
            projects = await GetProjectsAsync(source.OrgOrCollection, pat, ct);

        if (isTfs)
            await RunTfsInventoryAsync(source.OrgOrCollection, source.Project, pat, allProjects, summaries, ct);
        else
            await RunInventoryAsync(source.OrgOrCollection, projects, pat, summaries, ct);
    }

    // ── Mode 2: organisations-based ───────────────────────────────────────────

    private async Task RunMode2Async(
        List<OrganisationEntry> entries,
        Dictionary<string, InventorySummary> summaries,
        CancellationToken ct)
    {
        foreach (var entry in entries.Where(e => e.Enabled))
        {
            var pat = TokenResolver.Resolve(entry.Authentication?.AccessToken) ?? string.Empty;

            List<string> projects;
            if (entry.Projects.Count > 0)
                projects = entry.Projects;
            else
                projects = await GetProjectsAsync(entry.OrgOrCollection, pat, ct);

            await RunInventoryAsync(entry.OrgOrCollection, projects, pat, summaries, ct);
        }
    }

    // ── TFS subprocess inventory ───────────────────────────────────────────────

    private async Task RunTfsInventoryAsync(
        string collectionUrl,
        string? project,
        string pat,
        bool allProjects,
        Dictionary<string, InventorySummary> summaries,
        CancellationToken ct)
    {
        var table = BuildTable(summaries.Values);

        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                await foreach (var evt in _tfsAdapter.RunAsync(
                    collectionUrl, project, pat, allProjects, ct))
                {
                    var key = $"{collectionUrl}|{evt.ProjectName}";
                    if (!summaries.TryGetValue(key, out var summary))
                    {
                        summary = new InventorySummary
                        {
                            OrgOrCollection = collectionUrl,
                            ProjectName = evt.ProjectName
                        };
                        summaries[key] = summary;
                    }

                    summary.WorkItemsCount = evt.WorkItemsCount;
                    summary.RevisionsCount = evt.RevisionsCount;
                    summary.LastUpdatedUtc = evt.Timestamp;
                    if (evt.IsComplete)
                    {
                        summary.IsComplete = true;
                        summary.Error = evt.Error;
                    }

                    ctx.UpdateTarget(BuildTable(summaries.Values));
                }
            });
    }

    // ── Shared inventory loop ─────────────────────────────────────────────────

    private async Task RunInventoryAsync(
        string orgOrCollection,
        List<string> projects,
        string pat,
        Dictionary<string, InventorySummary> summaries,
        CancellationToken ct)
    {
        foreach (var p in projects)
        {
            var key = $"{orgOrCollection}|{p}";
            summaries[key] = new InventorySummary
            {
                OrgOrCollection = orgOrCollection,
                ProjectName = p
            };
        }

        var table = BuildTable(summaries.Values);

        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                foreach (var project in projects)
                {
                    var key = $"{orgOrCollection}|{project}";
                    var summary = summaries[key];

                    try
                    {
                        await foreach (var evt in _inventoryService.CountWorkItemsAsync(
                            orgOrCollection, project, pat, ct))
                        {
                            summary.WorkItemsCount = evt.WorkItemsCount;
                            summary.RevisionsCount = evt.RevisionsCount;
                            summary.LastUpdatedUtc = evt.Timestamp;
                            if (evt.IsComplete)
                            {
                                summary.IsComplete = true;
                                summary.Error = evt.Error;
                            }

                            ctx.UpdateTarget(BuildTable(summaries.Values));
                        }
                    }
                    catch (Exception ex)
                    {
                        summary.IsComplete = true;
                        summary.Error = ex.Message;
                        ctx.UpdateTarget(BuildTable(summaries.Values));
                    }
                }
            });
    }

    // ── Project enumeration ───────────────────────────────────────────────────

    private static async Task<List<string>> GetProjectsAsync(
        string orgOrCollection, string pat, CancellationToken ct)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var projectClient = await connection.GetClientAsync<ProjectHttpClient>(ct);
        var projects = await projectClient.GetProjects();
        return projects.Select(p => p.Name).ToList();
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
            .AddColumn(new TableColumn("Pipelines").RightAligned())
            .AddColumn("Status");

        foreach (var s in summaries)
        {
            var status = s.Error != null
                ? "[red]Error[/]"
                : s.IsComplete
                    ? "[green]✓[/]"
                    : "[grey]…[/]";

            table.AddRow(
                Markup.Escape(s.OrgOrCollection),
                Markup.Escape(s.ProjectName),
                s.WorkItemsCount.ToString(),
                s.RevisionsCount.ToString(),
                s.ReposCount.ToString(),
                s.PipelinesCount.ToString(),
                status);
        }

        return table;
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    private static void WriteCsv(IEnumerable<InventorySummary> summaries, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var writer = new StreamWriter(path, append: false, encoding: new UTF8Encoding(false));
        writer.WriteLine("OrgOrCollection,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc");

        foreach (var s in summaries)
        {
            writer.WriteLine(
                $"{Csv(s.OrgOrCollection)},{Csv(s.ProjectName)}," +
                $"{s.WorkItemsCount},{s.RevisionsCount}," +
                $"{s.ReposCount},{s.PipelinesCount}," +
                $"{s.IsComplete},{Csv(s.Error ?? string.Empty)}," +
                $"{s.LastUpdatedUtc:O}");
        }
    }

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
