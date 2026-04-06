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
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

public sealed class InventoryCommand : CommandBase<InventoryCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--output <PATH>")]
        [Description("Directory where discovery-summary.csv is written (default: current working directory)")]
        public string? OutputPath { get; set; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddAzureDevOpsInventory(config);
            services.AddSingleton<ITfsInventoryProvider, TfsInventoryProcessAdapter>();
        });

        var inventoryService = GetRequiredService<IInventoryService>();
        var summaries = new Dictionary<string, InventorySummary>(StringComparer.OrdinalIgnoreCase);
        var table = BuildTable(summaries.Values);

        await AnsiConsole.Live(table)
            .StartAsync(async ctx =>
            {
                await foreach (var evt in inventoryService.RunInventoryAsync(cancellationToken))
                {
                    var key = $"{evt.OrgOrCollection}|{evt.ProjectName}";
                    if (!summaries.TryGetValue(key, out var summary))
                    {
                        summary = new InventorySummary
                        {
                            OrgOrCollection = evt.OrgOrCollection,
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

        var outputDir = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? Directory.GetCurrentDirectory()
            : settings.OutputPath;

        var csvPath = Path.Combine(outputDir, "discovery-summary.csv");
        WriteCsv(summaries.Values, csvPath);

        AnsiConsole.MarkupLine($"\n[green]✅ Inventory complete.[/] CSV written to [blue]{Markup.Escape(csvPath)}[/]");
        return 0;
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
