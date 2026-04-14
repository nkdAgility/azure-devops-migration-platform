using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

/// <summary>
/// CLI command: discovery dependencies
/// Analyses configured organisations for cross-project and cross-organisation work item links.
/// Writes results to CSV and optional Mermaid diagram.
/// </summary>
public sealed class DependencyCommand : CommandBase<DependencyCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--output")]
        public string? OutputPath { get; set; }

        [CommandOption("--wiql")]
        public string? WiqlFilter { get; set; }

        [CommandOption("--output-projects")]
        public string? OutputProjectsPath { get; set; }

        [CommandOption("--output-diagram")]
        public string? OutputDiagramPath { get; set; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddAzureDevOpsDependencyAnalysis(config);
        });

        var logger = GetRequiredService<ILogger<DependencyCommand>>();

        try
        {
            var outputPath = settings.OutputPath ?? Path.Combine(Directory.GetCurrentDirectory(), "discovery-dependencies.csv");

            logger.LogInformation("Starting dependency discovery");
            logger.LogInformation("Output path: {OutputPath}", outputPath);

            if (settings.WiqlFilter != null)
                logger.LogInformation("WIQL filter: {WiqlFilter}", settings.WiqlFilter);

            // Resolve the discovery service
            var discoveryService = GetRequiredService<IDependencyDiscoveryService>();

            // Check for file overwrite
            if (File.Exists(outputPath))
                AnsiConsole.MarkupLine($"[yellow]⚠ Overwriting existing file: {outputPath}[/]");

            var crossProjectCount = 0;
            var crossOrgCount = 0;
            var workItemsAnalysed = 0;

            // Write CSV header and stream records
            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream))
            {
                writer.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");

                await foreach (var evt in discoveryService.DiscoverDependenciesAsync(settings.WiqlFilter, cancellationToken))
                {
                    if (evt is DependencyFoundEvent foundEvent)
                    {
                        var record = foundEvent.Record;
                        writer.WriteLine($"{record.SourceWorkItemId},{record.SourceWorkItemType},{record.SourceProject},{record.LinkType},{record.LinkScope},{record.TargetWorkItemId},{record.TargetProject},{record.TargetOrganisation},{record.TargetStatus}");

                        if (record.LinkScope == LinkScope.CrossProject)
                            crossProjectCount++;
                        else
                            crossOrgCount++;
                    }
                    else if (evt is DependencyHeartbeatEvent heartbeat)
                    {
                        workItemsAnalysed = heartbeat.WorkItemsAnalysed;
                    }
                }

                writer.Flush();
            }

            // Print summary
            var totalLinks = crossProjectCount + crossOrgCount;

            if (totalLinks == 0)
            {
                AnsiConsole.MarkupLine("[green]✓[/] No external dependencies found.");
            }
            else
            {
                AnsiConsole.WriteLine();
                var table = new Table();
                table.AddColumn("Metric");
                table.AddColumn("Count");
                table.AddRow("Work Items Analysed", workItemsAnalysed.ToString());
                table.AddRow("Total External Links", totalLinks.ToString());
                table.AddRow("Cross-Project Links", crossProjectCount.ToString());
                table.AddRow($"[red]⚠ Cross-Organisation Links[/]", $"[red]{crossOrgCount}[/]");
                table.AddRow("Report File", Path.GetFileName(outputPath));

                AnsiConsole.Write(table);

                if (crossOrgCount > 0)
                    AnsiConsole.MarkupLine($"[red]⚠ ACTION REQUIRED: {crossOrgCount} cross-organisation link(s) will break after migration[/]");
            }

            logger.LogInformation("Dependency discovery completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dependency discovery failed");
            AnsiConsole.MarkupLine($"[red]✗ Error: {ex.Message}[/]");
            return 1;
        }
    }
}
