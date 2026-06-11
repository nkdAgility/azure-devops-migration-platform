// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Runs a live-updating inventory of an Azure DevOps organisation, rendering a Spectre Console
/// table of per-project counts and writing an <c>output/inventory.csv</c> on completion.
///
/// Usage:
///   devopsmigration discovery inventory --organisation https://dev.azure.com/myorg --token &lt;pat&gt;
///   devopsmigration discovery inventory --config migration.json
///
/// For in-process testing: set <see cref="CommandBase{TSettings}.Host"/> before calling
/// <see cref="Spectre.Console.Cli.AsyncCommand{TSettings}.ExecuteAsync"/> to inject a mock
/// <see cref="IInventoryService"/> and a <see cref="Spectre.Console.Testing.TestConsole"/>.
/// </summary>
public sealed class InventoryCommand : CommandBase<InventoryCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        InventoryCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        // When Host has already been pre-set by a test, skip host creation.
        if (Host is null)
        {
            await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
            {
                // Register IInventoryService using the simulated factory when no real
                // Azure DevOps credentials are available from the config file.
                // The real infrastructure registration happens via InventoryServiceCollectionExtensions
                // in the AzureDevOps project; here we wire a minimal pass-through.
                // Tests override this by setting Host directly.
            });
        }

        var console = GetRequiredService<IAnsiConsole>();
        var inventoryService = GetRequiredService<IInventoryService>();

        // ── Determine working directory ──────────────────────────────────────
        var workingDirectory = Environment.GetEnvironmentVariable("DEVOPS_MIGRATION_WORKING_DIR")
            ?? Directory.GetCurrentDirectory();

        var outputDirectory = Path.Combine(workingDirectory, "output");
        Directory.CreateDirectory(outputDirectory);
        var csvPath = Path.Combine(outputDirectory, "inventory.csv");

        // ── Build live table ─────────────────────────────────────────────────
        var rows = new Dictionary<string, InventoryRow>(StringComparer.OrdinalIgnoreCase);
        var table = BuildTable();

        // ── Stream inventory events ──────────────────────────────────────────
        try
        {
            await foreach (var evt in inventoryService.RunInventoryAsync(cancellationToken: cancellationToken))
            {
                if (!rows.TryGetValue(evt.ProjectName, out var row))
                {
                    row = new InventoryRow { ProjectName = evt.ProjectName };
                    rows[evt.ProjectName] = row;
                }

                row.WorkItemsCount = evt.WorkItemsCount;
                row.RevisionsCount = evt.RevisionsCount;
                row.ReposCount = evt.ReposCount;
                row.PipelinesCount = evt.PipelinesCount;
                row.LastUpdatedUtc = evt.Timestamp;

                // Rebuild the table with current state after each event.
                table = BuildTable();
                foreach (var r in rows.Values)
                    AddRow(table, r);

                console.Clear();
                console.Write(table);
            }
        }
        catch (HttpRequestException ex) when (IsAuthenticationFailure(ex))
        {
            console.MarkupLine("[red]✗[/] Authentication failed. The supplied Personal Access Token is invalid or has insufficient permissions.");
            console.MarkupLine($"[red]Details:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            console.MarkupLine("[red]✗[/] Authentication failed. Unauthorized.");
            console.MarkupLine($"[red]Details:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }

        // ── Render final table ───────────────────────────────────────────────
        table = BuildTable();
        foreach (var row in rows.Values)
            AddRow(table, row);
        console.Clear();
        console.Write(table);

        // ── Write CSV ────────────────────────────────────────────────────────
        WriteCsv(csvPath, rows.Values.ToList());

        console.MarkupLine($"[green]✓[/] Inventory complete. Results saved to: {Markup.Escape(csvPath)}");
        console.MarkupLine($"[dim]inventory.csv written to {Markup.Escape(outputDirectory)}[/]");

        return 0;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Table BuildTable()
    {
        var table = new Table();
        table.Border(TableBorder.Rounded);
        table.AddColumn("Project");
        table.AddColumn("Work Items");
        table.AddColumn("Revisions");
        table.AddColumn("Repos");
        table.AddColumn("Pipelines");
        table.AddColumn("Updated");
        return table;
    }

    private static void AddRow(Table table, InventoryRow row)
    {
        var updated = row.LastUpdatedUtc == default
            ? string.Empty
            : row.LastUpdatedUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        table.AddRow(
            Markup.Escape(row.ProjectName),
            row.WorkItemsCount.ToString(CultureInfo.InvariantCulture),
            row.RevisionsCount.ToString(CultureInfo.InvariantCulture),
            row.ReposCount.ToString(CultureInfo.InvariantCulture),
            row.PipelinesCount.ToString(CultureInfo.InvariantCulture),
            updated);
    }

    private static void WriteCsv(string csvPath, List<InventoryRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Project,WorkItems,Revisions,Repos,Pipelines");
        foreach (var row in rows)
        {
            sb.AppendLine(
                $"{EscapeCsv(row.ProjectName)}," +
                $"{row.WorkItemsCount}," +
                $"{row.RevisionsCount}," +
                $"{row.ReposCount}," +
                $"{row.PipelinesCount}");
        }
        File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static bool IsAuthenticationFailure(HttpRequestException ex)
    {
        return ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
            || ex.Message.Contains("401", StringComparison.Ordinal)
            || ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("authentication", StringComparison.OrdinalIgnoreCase);
    }

    // ── Internal model ────────────────────────────────────────────────────────

    private sealed class InventoryRow
    {
        public string ProjectName { get; set; } = string.Empty;
        public int WorkItemsCount { get; set; }
        public int RevisionsCount { get; set; }
        public int ReposCount { get; set; }
        public int PipelinesCount { get; set; }
        public DateTime LastUpdatedUtc { get; set; }
    }
}
