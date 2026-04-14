using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        [CommandOption("--output <PATH>")]
        [System.ComponentModel.Description("Base directory where discovery output is organized by organisation and project (default: ./output)")]
        public string? OutputPath { get; set; }

        [CommandOption("--wiql")]
        public string? WiqlFilter { get; set; }
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
            var baseOutputDir = string.IsNullOrWhiteSpace(settings.OutputPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "output")
                : settings.OutputPath;

            logger.LogInformation("Starting dependency discovery");
            logger.LogInformation("Base output directory: {BaseOutputDir}", baseOutputDir);

            if (settings.WiqlFilter != null)
                logger.LogInformation("WIQL filter: {WiqlFilter}", settings.WiqlFilter);

            // Resolve the discovery service
            var discoveryService = GetRequiredService<IDependencyDiscoveryService>();

            var crossProjectCount = 0;
            var crossOrgCount = 0;
            var workItemsAnalysed = 0;
            var projectAccumulator = new Dictionary<ProjectPairKey, int>();
            var perProjectDependencies = new Dictionary<string, Dictionary<ProjectPairKey, int>>();  // project → dependencies
            var projectOrganisationMap = new Dictionary<string, string>();  // project → org  

            // Write CSV header and stream records
            var fullCsvPath = Path.Combine(baseOutputDir, "dependencies.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(fullCsvPath) ?? baseOutputDir);

            using (var fileStream = new FileStream(fullCsvPath, FileMode.Create, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream))
            {
                writer.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");

                await foreach (var evt in discoveryService.DiscoverDependenciesAsync(settings.WiqlFilter, cancellationToken))
                {
                    if (evt is DependencyFoundEvent foundEvent)
                    {
                        var record = foundEvent.Record;
                        writer.WriteLine($"{record.SourceWorkItemId},{record.SourceWorkItemType},{record.SourceProject},{record.LinkType},{record.LinkScope},{record.TargetWorkItemId},{record.TargetProject},{record.TargetOrganisation},{record.TargetStatus}");

                        // Accumulate project pair statistics
                        var key = new ProjectPairKey(record);
                        if (!projectAccumulator.ContainsKey(key))
                            projectAccumulator[key] = 0;
                        projectAccumulator[key]++;

                        // Track per-project dependencies
                        if (!perProjectDependencies.ContainsKey(record.SourceProject))
                            perProjectDependencies[record.SourceProject] = new Dictionary<ProjectPairKey, int>();
                        if (!perProjectDependencies[record.SourceProject].ContainsKey(key))
                            perProjectDependencies[record.SourceProject][key] = 0;
                        perProjectDependencies[record.SourceProject][key]++;

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
                logger.LogInformation("Dependencies CSV written to {FullCsvPath}", fullCsvPath);
            }

            // Convert accumulator to ProjectDependencyRecord list
            var projectPairs = projectAccumulator
                .Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value))
                .ToList();

            // Assign GroupIds via Union-Find
            if (projectPairs.Count > 0)
            {
                var componentIds = UnionFindComponentLabeler.AssignComponentIds(projectPairs);
                foreach (var pair in projectPairs)
                {
                    var projectName = pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation)
                        ? $"{pair.TargetOrganisation}#{pair.TargetProject ?? ""}"
                        : pair.TargetProject;

                    if (!string.IsNullOrWhiteSpace(projectName) && componentIds.TryGetValue(projectName, out var groupId))
                        pair.GroupId = groupId;

                    // Also assign to source
                    if (componentIds.TryGetValue(pair.SourceProject, out var sourceGroupId))
                        pair.GroupId = sourceGroupId;
                }

                // Write per-project dependency files
                foreach (var (sourceProject, deps) in perProjectDependencies)
                {
                    var orgName = "unknown";  // Will be populated from configuration or discovered org name
                    var projectDepsPath = Path.Combine(baseOutputDir, orgName, sourceProject, "dependencies.csv");
                    Directory.CreateDirectory(Path.GetDirectoryName(projectDepsPath) ?? baseOutputDir);

                    using (var fileStream = new FileStream(projectDepsPath, FileMode.Create, FileAccess.Write))
                    using (var writer = new StreamWriter(fileStream))
                    {
                        writer.WriteLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId");
                        var projectPairs_ = deps.Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value)).ToList();
                        foreach (var pair in projectPairs_.OrderByDescending(p => p.LinkCount))
                        {
                            writer.WriteLine($"{pair.SourceProject},{pair.TargetProject},{pair.TargetOrganisation},{pair.LinkCount},{pair.LinkScope},{pair.GroupId}");
                        }
                        writer.Flush();
                    }
                    logger.LogInformation("Project dependencies written to {ProjectDepsPath}", projectDepsPath);
                }

                // Generate Mermaid diagram at base level
                var aggregatedDiagramPath = Path.Combine(baseOutputDir, "dependencies.md");
                var diagram = new MermaidDiagramBuilder(projectPairs).Build();
                File.WriteAllText(aggregatedDiagramPath, diagram);
                logger.LogInformation("Mermaid diagram written to {AggregatedDiagramPath}", aggregatedDiagramPath);
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
                var summaryTable = new Table();
                summaryTable.Title = new TableTitle("[bold]Discovery Summary[/]");
                summaryTable.AddColumn("Metric");
                summaryTable.AddColumn("Count");
                summaryTable.AddRow("Work Items Analysed", workItemsAnalysed.ToString());
                summaryTable.AddRow("Total External Links", totalLinks.ToString());
                summaryTable.AddRow("Cross-Project Links", crossProjectCount.ToString());
                summaryTable.AddRow($"[red]⚠ Cross-Organisation Links[/]", $"[red]{crossOrgCount}[/]");
                summaryTable.AddRow("Output Directory", baseOutputDir);

                AnsiConsole.Write(summaryTable);

                if (crossOrgCount > 0)
                    AnsiConsole.MarkupLine($"[red]⚠ ACTION REQUIRED: {crossOrgCount} cross-organisation link(s) will break after migration[/]");

                // Print project dependency table
                if (projectPairs.Count > 0)
                {
                    AnsiConsole.WriteLine();
                    var projectTable = new Table();
                    projectTable.Title = new TableTitle("[bold]Project Dependency Map[/]");
                    projectTable.AddColumn("Source Project");
                    projectTable.AddColumn("Target Project");
                    projectTable.AddColumn("Links");
                    projectTable.AddColumn("Scope");

                    foreach (var pair in projectPairs.OrderByDescending(p => p.LinkCount))
                    {
                        var targetDisplay = pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation)
                            ? $"🌐 {pair.TargetOrganisation}/{pair.TargetProject}"
                            : pair.TargetProject;

                        var scopeDisplay = pair.LinkScope == LinkScope.CrossOrganisation
                            ? "[red]Cross-Org[/]"
                            : "Cross-Project";

                        projectTable.AddRow(pair.SourceProject, targetDisplay, pair.LinkCount.ToString(), scopeDisplay);
                    }

                    AnsiConsole.Write(projectTable);
                    AnsiConsole.MarkupLine($"[green]✓[/] Project dependencies written to [blue]{baseOutputDir}[/]");
                    AnsiConsole.MarkupLine($"[green]✓[/] Dependency diagram written to [blue]{baseOutputDir}[/]");
                }
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
