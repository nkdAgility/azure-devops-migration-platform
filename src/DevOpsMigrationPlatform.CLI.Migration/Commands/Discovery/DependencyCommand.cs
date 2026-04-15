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
        var console = GetRequiredService<IAnsiConsole>();

        try
        {
            var baseOutputDir = string.IsNullOrWhiteSpace(settings.OutputPath)
                ? Path.Combine(Directory.GetCurrentDirectory(), "output")
                : settings.OutputPath;

            logger.LogInformation("Starting dependency discovery");
            logger.LogInformation("Base output directory: {BaseOutputDir}", baseOutputDir);

            if (settings.WiqlFilter != null)
                logger.LogInformation("WIQL filter: {WiqlFilter}", settings.WiqlFilter);

            var discoveryService = GetRequiredService<IDependencyDiscoveryService>();

            var crossProjectCount = 0;
            var crossOrgCount = 0;
            var workItemsAnalysed = 0;

            // WI-level records: (orgName, project) → list
            var perProjectRecords = new Dictionary<(string, string), List<DependencyRecord>>();
            // Pair-level counts: (orgName, project) → ProjectPairKey → count
            var perProjectPairs = new Dictionary<(string, string), Dictionary<ProjectPairKey, int>>();
            // Org-level pair counts: orgName → ProjectPairKey → count
            var perOrgPairs = new Dictionary<string, Dictionary<ProjectPairKey, int>>();
            // Live progress per (org, project)
            var progressState = new Dictionary<(string, string), ProjectProgress>();

            // ── Root dependencies.csv (raw WI-level for test assertions) ─────
            var rootCsvPath = Path.Combine(baseOutputDir, "dependencies.csv");
            Directory.CreateDirectory(baseOutputDir);

            using (var rootStream = new FileStream(rootCsvPath, FileMode.Create, FileAccess.Write))
            using (var rootWriter = new StreamWriter(rootStream))
            {
                rootWriter.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");

                if (console.Profile.Capabilities.Interactive)
                {
                    var liveTable = BuildProgressTable(progressState.Values);
                    await console.Live(liveTable)
                        .AutoClear(false)
                        .Overflow(VerticalOverflow.Ellipsis)
                        .StartAsync(async ctx =>
                        {
                            await foreach (var evt in discoveryService.DiscoverDependenciesAsync(settings.WiqlFilter, cancellationToken))
                            {
                                if (evt is DependencyFoundEvent foundEvent)
                                {
                                    var record = foundEvent.Record;
                                    rootWriter.WriteLine(
                                        $"{record.SourceWorkItemId},{record.SourceWorkItemType},{record.SourceProject}," +
                                        $"{record.LinkType},{record.LinkScope},{record.TargetWorkItemId}," +
                                        $"{record.TargetProject},{record.TargetOrganisation},{record.TargetStatus}");

                                    var orgName = ExtractOrgName(record.SourceOrganisationUrl);
                                    var project = record.SourceProject ?? "unknown";
                                    var key = new ProjectPairKey(record);
                                    var projKey = (orgName, project);

                                    if (!perProjectRecords.TryGetValue(projKey, out var records))
                                    {
                                        records = new List<DependencyRecord>();
                                        perProjectRecords[projKey] = records;
                                    }
                                    records.Add(record);

                                    if (!perProjectPairs.TryGetValue(projKey, out var projPairs))
                                    {
                                        projPairs = new Dictionary<ProjectPairKey, int>();
                                        perProjectPairs[projKey] = projPairs;
                                    }
                                    projPairs[key] = projPairs.TryGetValue(key, out var pc) ? pc + 1 : 1;

                                    if (!perOrgPairs.TryGetValue(orgName, out var orgPairs))
                                    {
                                        orgPairs = new Dictionary<ProjectPairKey, int>();
                                        perOrgPairs[orgName] = orgPairs;
                                    }
                                    orgPairs[key] = orgPairs.TryGetValue(key, out var oc) ? oc + 1 : 1;

                                    if (record.LinkScope == LinkScope.CrossProject)
                                        crossProjectCount++;
                                    else
                                        crossOrgCount++;
                                }
                                else if (evt is DependencyHeartbeatEvent heartbeat)
                                {
                                    workItemsAnalysed = Math.Max(workItemsAnalysed, heartbeat.WorkItemsAnalysed);

                                    var orgName = ExtractOrgName(heartbeat.OrganisationUrl);
                                    var projKey = (orgName, heartbeat.ProjectName);
                                    if (!progressState.TryGetValue(projKey, out var progress))
                                    {
                                        progress = new ProjectProgress
                                        {
                                            OrgName = orgName,
                                            ProjectName = heartbeat.ProjectName
                                        };
                                        progressState[projKey] = progress;
                                    }
                                    progress.WorkItemsAnalysed = heartbeat.WorkItemsAnalysed;
                                    progress.CrossProjectLinks = heartbeat.CrossProjectCount;
                                    progress.CrossOrgLinks = heartbeat.CrossOrgCount;
                                    progress.IsComplete = heartbeat.IsComplete;

                                    ctx.UpdateTarget(BuildProgressTable(progressState.Values));
                                }
                            }
                        });
                }
                else
                {
                    // Non-interactive: plain line output on each heartbeat completion
                    await foreach (var evt in discoveryService.DiscoverDependenciesAsync(settings.WiqlFilter, cancellationToken))
                    {
                        if (evt is DependencyFoundEvent foundEvent)
                        {
                            var record = foundEvent.Record;
                            rootWriter.WriteLine(
                                $"{record.SourceWorkItemId},{record.SourceWorkItemType},{record.SourceProject}," +
                                $"{record.LinkType},{record.LinkScope},{record.TargetWorkItemId}," +
                                $"{record.TargetProject},{record.TargetOrganisation},{record.TargetStatus}");

                            var orgName = ExtractOrgName(record.SourceOrganisationUrl);
                            var project = record.SourceProject ?? "unknown";
                            var key = new ProjectPairKey(record);
                            var projKey = (orgName, project);

                            if (!perProjectRecords.TryGetValue(projKey, out var records))
                            {
                                records = new List<DependencyRecord>();
                                perProjectRecords[projKey] = records;
                            }
                            records.Add(record);

                            if (!perProjectPairs.TryGetValue(projKey, out var projPairs))
                            {
                                projPairs = new Dictionary<ProjectPairKey, int>();
                                perProjectPairs[projKey] = projPairs;
                            }
                            projPairs[key] = projPairs.TryGetValue(key, out var pc) ? pc + 1 : 1;

                            if (!perOrgPairs.TryGetValue(orgName, out var orgPairs))
                            {
                                orgPairs = new Dictionary<ProjectPairKey, int>();
                                perOrgPairs[orgName] = orgPairs;
                            }
                            orgPairs[key] = orgPairs.TryGetValue(key, out var oc) ? oc + 1 : 1;

                            if (record.LinkScope == LinkScope.CrossProject)
                                crossProjectCount++;
                            else
                                crossOrgCount++;
                        }
                        else if (evt is DependencyHeartbeatEvent heartbeat)
                        {
                            workItemsAnalysed = Math.Max(workItemsAnalysed, heartbeat.WorkItemsAnalysed);
                            if (heartbeat.IsComplete)
                            {
                                var orgName = ExtractOrgName(heartbeat.OrganisationUrl);
                                console.MarkupLine(
                                    $"  [grey]{Markup.Escape(orgName)}[/] / [white]{Markup.Escape(heartbeat.ProjectName)}[/]: " +
                                    $"{heartbeat.WorkItemsAnalysed} items analysed, " +
                                    $"{heartbeat.CrossProjectCount} cross-project, " +
                                    $"[red]{heartbeat.CrossOrgCount}[/] cross-org — [green]✓[/]");
                            }
                        }
                    }
                }

                rootWriter.Flush();
            }

            logger.LogInformation("Root dependencies CSV written to {Path}", rootCsvPath);

            // ── Per-project output ───────────────────────────────────────────
            foreach (var ((orgName, project), records) in perProjectRecords)
            {
                var projDir = Path.Combine(baseOutputDir, orgName, project);
                Directory.CreateDirectory(projDir);

                var projDepsCsv = Path.Combine(projDir, "dependencies.csv");
                using (var fs = new FileStream(projDepsCsv, FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(fs))
                {
                    w.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");
                    foreach (var r in records)
                        w.WriteLine(
                            $"{r.SourceWorkItemId},{r.SourceWorkItemType},{r.SourceProject}," +
                            $"{r.LinkType},{r.LinkScope},{r.TargetWorkItemId}," +
                            $"{r.TargetProject},{r.TargetOrganisation},{r.TargetStatus}");
                    w.Flush();
                }

                var projPairs = perProjectPairs[(orgName, project)]
                    .Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value))
                    .OrderByDescending(p => p.LinkCount)
                    .ToList();

                var groupedCsv = Path.Combine(projDir, "grouped.csv");
                using (var fs = new FileStream(groupedCsv, FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(fs))
                {
                    w.WriteLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope");
                    foreach (var pair in projPairs)
                        w.WriteLine($"{pair.SourceProject},{pair.TargetProject},{pair.TargetOrganisation},{pair.LinkCount},{pair.LinkScope}");
                    w.Flush();
                }

                File.WriteAllText(
                    Path.Combine(projDir, "dependencies.md"),
                    new MermaidDiagramBuilder(projPairs).Build());

                logger.LogInformation("Project output written to {ProjDir}", projDir);
            }

            // ── Per-org output ───────────────────────────────────────────────
            foreach (var (orgName, orgPairMap) in perOrgPairs)
            {
                var orgDir = Path.Combine(baseOutputDir, orgName);
                Directory.CreateDirectory(orgDir);

                var orgPairs = orgPairMap
                    .Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value))
                    .OrderByDescending(p => p.LinkCount)
                    .ToList();

                var componentIds = UnionFindComponentLabeler.AssignComponentIds(orgPairs);
                foreach (var pair in orgPairs)
                {
                    if (componentIds.TryGetValue(pair.SourceProject, out var gid))
                        pair.GroupId = gid;
                }

                var orgDepsCsv = Path.Combine(orgDir, "dependencies.csv");
                using (var fs = new FileStream(orgDepsCsv, FileMode.Create, FileAccess.Write))
                using (var w = new StreamWriter(fs))
                {
                    w.WriteLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId");
                    foreach (var pair in orgPairs)
                        w.WriteLine($"{pair.SourceProject},{pair.TargetProject},{pair.TargetOrganisation},{pair.LinkCount},{pair.LinkScope},{pair.GroupId}");
                    w.Flush();
                }

                File.WriteAllText(
                    Path.Combine(orgDir, "dependencies.md"),
                    new MermaidDiagramBuilder(orgPairs).Build());

                logger.LogInformation("Org output written to {OrgDir}", orgDir);
            }

            // ── Console summary ──────────────────────────────────────────────
            var totalLinks = crossProjectCount + crossOrgCount;

            if (totalLinks == 0)
            {
                console.MarkupLine("[green]✓[/] No external dependencies found.");
            }
            else
            {
                console.WriteLine();
                var summaryTable = new Table()
                    .Title("[bold]Discovery Summary[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Grey)
                    .AddColumn("Metric")
                    .AddColumn(new TableColumn("Count").RightAligned());
                summaryTable.AddRow("Work Items Analysed", workItemsAnalysed.ToString());
                summaryTable.AddRow("Total External Links", totalLinks.ToString());
                summaryTable.AddRow("Cross-Project Links", crossProjectCount.ToString());
                summaryTable.AddRow("[red]⚠ Cross-Organisation Links[/]", $"[red]{crossOrgCount}[/]");
                summaryTable.AddRow("Output Directory", Markup.Escape(baseOutputDir));
                console.Write(summaryTable);

                if (crossOrgCount > 0)
                    console.MarkupLine($"[red]⚠ ACTION REQUIRED: {crossOrgCount} cross-organisation link(s) will break after migration[/]");

                var allPairs = perOrgPairs.Values
                    .SelectMany(d => d.Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value)))
                    .OrderByDescending(p => p.LinkCount)
                    .ToList();

                if (allPairs.Count > 0)
                {
                    console.WriteLine();
                    var projectTable = new Table()
                        .Title("[bold]Project Dependency Map[/]")
                        .RoundedBorder()
                        .BorderColor(Color.Grey)
                        .AddColumn("Source Project")
                        .AddColumn("Target Project")
                        .AddColumn(new TableColumn("Links").RightAligned())
                        .AddColumn("Scope");

                    foreach (var pair in allPairs)
                    {
                        var targetDisplay = pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation)
                            ? $"🌐 {Markup.Escape(pair.TargetOrganisation)}/{Markup.Escape(pair.TargetProject ?? "")}"
                            : Markup.Escape(pair.TargetProject ?? "");
                        var scopeDisplay = pair.LinkScope == LinkScope.CrossOrganisation
                            ? "[red]Cross-Org[/]"
                            : "Cross-Project";
                        projectTable.AddRow(
                            Markup.Escape(pair.SourceProject),
                            targetDisplay,
                            pair.LinkCount.ToString(),
                            scopeDisplay);
                    }

                    console.Write(projectTable);
                    console.MarkupLine($"[green]✓[/] Project dependencies written to [blue]{Markup.Escape(baseOutputDir)}[/]");
                    console.MarkupLine($"[green]✓[/] Dependency diagram written to [blue]{Markup.Escape(baseOutputDir)}[/]");
                }
            }

            logger.LogInformation("Dependency discovery completed successfully");
            console.MarkupLine($"[green]✓[/] Dependency discovery completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Dependency discovery failed");
            console.MarkupLine($"[red]✗ Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    /// <summary>
    /// Extracts a short org name from a full org URL for use as a folder name.
    /// "https://dev.azure.com/contoso" → "contoso"
    /// "https://contoso.visualstudio.com" → "contoso"
    /// Falls back to the sanitised host if neither pattern matches.
    /// </summary>
    private static string ExtractOrgName(string orgUrl)
    {
        if (string.IsNullOrWhiteSpace(orgUrl))
            return "unknown";

        if (!Uri.TryCreate(orgUrl, UriKind.Absolute, out var uri))
            return SanitiseFolderName(orgUrl);

        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
                return SanitiseFolderName(segments[0]);
        }

        var hostParts = uri.Host.Split('.');
        if (hostParts.Length >= 3 && hostParts[^2].Equals("visualstudio", StringComparison.OrdinalIgnoreCase))
            return SanitiseFolderName(hostParts[0]);

        return SanitiseFolderName(uri.Host);
    }

    private static string SanitiseFolderName(string name)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(name, @"[^\w\-]", "_");
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static Table BuildProgressTable(IEnumerable<ProjectProgress> state)
    {
        var table = new Table()
            .Title("[bold yellow]Dependency Discovery[/]")
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Organisation")
            .AddColumn("Project")
            .AddColumn(new TableColumn("Analysed").RightAligned())
            .AddColumn(new TableColumn("Cross-Project").RightAligned())
            .AddColumn(new TableColumn("Cross-Org").RightAligned())
            .AddColumn("Status");

        foreach (var p in state)
        {
            var status = p.IsComplete ? "[green]✓[/]" : "[grey]…[/]";
            var crossOrg = p.CrossOrgLinks > 0
                ? $"[red]{p.CrossOrgLinks}[/]"
                : p.CrossOrgLinks.ToString();
            table.AddRow(
                Markup.Escape(p.OrgName),
                Markup.Escape(p.ProjectName),
                p.WorkItemsAnalysed.ToString(),
                p.CrossProjectLinks.ToString(),
                crossOrg,
                status);
        }

        return table;
    }

    private sealed class ProjectProgress
    {
        public string OrgName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int WorkItemsAnalysed { get; set; }
        public int CrossProjectLinks { get; set; }
        public int CrossOrgLinks { get; set; }
        public bool IsComplete { get; set; }
    }
}
