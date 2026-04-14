using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

public sealed class InventoryCommand : CommandBase<InventoryCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--output <PATH>")]
        [Description("Directory where discovery-summary.csv is written (default: ./output)")]
        public string? OutputPath { get; set; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddAzureDevOpsInventory(config);
        });

        var console = GetRequiredService<IAnsiConsole>();
        var inventoryService = GetRequiredService<IInventoryService>();
        var summaries = new Dictionary<string, InventorySummary>(StringComparer.OrdinalIgnoreCase);

        if (console.Profile.Capabilities.Interactive)
        {
            var table = BuildTable(summaries.Values);
            await console.Live(table)
                .StartAsync(async ctx =>
                {
                    await foreach (var evt in inventoryService.RunInventoryAsync(cancellationToken))
                    {
                        UpdateSummary(summaries, evt);
                        ctx.UpdateTarget(BuildTable(summaries.Values));
                    }
                });
        }
        else
        {
            await foreach (var evt in inventoryService.RunInventoryAsync(cancellationToken))
            {
                UpdateSummary(summaries, evt);
                if (evt.IsComplete)
                {
                    var status = evt.Error != null ? "✗ Failed" : "✓";
                    console.MarkupLine($"  {Markup.Escape(evt.Url)} / {Markup.Escape(evt.ProjectName)}: {evt.WorkItemsCount} work items, {evt.RevisionsCount} revisions — {status}");
                }
            }
        }

        var baseOutputDir = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "output")
            : settings.OutputPath;

        var csvPath = Path.Combine(baseOutputDir, "discovery-summary.csv");
        WriteCsv(summaries.Values, csvPath);

        console.MarkupLine($"\n[green]✅ Inventory complete.[/] CSV written to [blue]{Markup.Escape(csvPath)}[/]");
        return 0;
    }

    private static void UpdateSummary(Dictionary<string, InventorySummary> summaries, InventoryProgressEvent evt)
    {
        var key = $"{evt.Url}|{evt.ProjectName}";
        if (!summaries.TryGetValue(key, out var summary))
        {
            summary = new InventorySummary { Url = evt.Url, ProjectName = evt.ProjectName };
            summaries[key] = summary;
        }

        summary.WorkItemsCount = evt.WorkItemsCount;
        summary.RevisionsCount = evt.RevisionsCount;
        summary.ReposCount = evt.ReposCount;
        summary.LastUpdatedUtc = evt.Timestamp;
        if (evt.IsComplete)
        {
            summary.IsComplete = true;
            summary.Error = evt.Error;
        }
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
                ? "[red]✗ Failed[/]"
                : s.IsComplete
                    ? "[green]✓[/]"
                    : "[grey]…[/]";

            // Prefix counts with ~ when the result is partial (error stopped the scan).
            var partial = s.Error != null && (s.WorkItemsCount > 0 || s.RevisionsCount > 0);
            var wiCount = partial ? $"~{s.WorkItemsCount}" : s.WorkItemsCount.ToString();
            var revCount = partial ? $"~{s.RevisionsCount}" : s.RevisionsCount.ToString();

            table.AddRow(
                Markup.Escape(s.Url),
                Markup.Escape(s.ProjectName),
                wiCount,
                revCount,
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
        writer.WriteLine("Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc");

        foreach (var s in summaries)
        {
            writer.WriteLine(
                $"{Csv(s.Url)},{Csv(s.ProjectName)}," +
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
