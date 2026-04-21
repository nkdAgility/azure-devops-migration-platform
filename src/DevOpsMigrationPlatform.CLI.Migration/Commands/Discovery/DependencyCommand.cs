using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
                            if (evt.Stage == "Completed")
                                break;

                            UpdateProgressFromEvent(progressState, evt);
                            ctx.UpdateTarget(BuildLivePanel(progressState.Values, startTime));
                        }
                    });
            }
            else
            {
                await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                {
                    if (evt.Stage == "Completed")
                        break;

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

        // ── Post-processing: read root CSV from package and generate output ──────
        var rootCsvPath = Path.Combine(outputPath, "dependencies.csv");
        if (!File.Exists(rootCsvPath))
        {
            console.MarkupLine("[green]✓[/] No external dependencies found.");
            console.MarkupLine($"\n[green]✓[/] Dependency discovery completed successfully.");
            return 0;
        }

        var records = ParseRootCsv(rootCsvPath);
        if (records.Count == 0)
        {
            console.MarkupLine("[green]✓[/] No external dependencies found.");
            console.MarkupLine($"\n[green]✓[/] Dependency discovery completed successfully.");
            return 0;
        }

        // Aggregate counts.
        var crossProjectCount = records.Count(r => r.LinkScope == LinkScope.CrossProject);
        var crossOrgCount = records.Count(r => r.LinkScope == LinkScope.CrossOrganisation);
        var workItemsAnalysed = progressState.Values.Sum(p => p.WorkItemsAnalysed);

        // Group by (org, project) for per-project/per-org output.
        var perProjectRecords = new Dictionary<(string, string), List<DependencyRecord>>();
        var perProjectPairs = new Dictionary<(string, string), Dictionary<ProjectPairKey, int>>();
        var perProjectPairDates = new Dictionary<(string, string), Dictionary<ProjectPairKey, DateTimeOffset?>>();
        var perProjectPairTypes = new Dictionary<(string, string), Dictionary<ProjectPairKey, Dictionary<string, int>>>();
        var perProjectPairActive = new Dictionary<(string, string), Dictionary<ProjectPairKey, int>>();
        var perOrgPairs = new Dictionary<string, Dictionary<ProjectPairKey, int>>();
        var perOrgPairDates = new Dictionary<string, Dictionary<ProjectPairKey, DateTimeOffset?>>();
        var perOrgPairTypes = new Dictionary<string, Dictionary<ProjectPairKey, Dictionary<string, int>>>();
        var perOrgPairActive = new Dictionary<string, Dictionary<ProjectPairKey, int>>();

        foreach (var r in records)
        {
            var orgName = ExtractOrgName(r.SourceOrganisationUrl);
            var project = r.SourceProject ?? "unknown";
            var projKey = (orgName, project);
            var pairKey = new ProjectPairKey(r);
            var wiType = string.IsNullOrWhiteSpace(r.SourceWorkItemType) ? "Unknown" : r.SourceWorkItemType;

            if (!perProjectRecords.TryGetValue(projKey, out var projRecords))
            {
                projRecords = new List<DependencyRecord>();
                perProjectRecords[projKey] = projRecords;
            }
            projRecords.Add(r);

            if (!perProjectPairs.TryGetValue(projKey, out var projPairMap))
            {
                projPairMap = new Dictionary<ProjectPairKey, int>();
                perProjectPairs[projKey] = projPairMap;
            }
            projPairMap[pairKey] = projPairMap.TryGetValue(pairKey, out var pc) ? pc + 1 : 1;

            if (!perProjectPairDates.TryGetValue(projKey, out var projDateMap))
            {
                projDateMap = new Dictionary<ProjectPairKey, DateTimeOffset?>();
                perProjectPairDates[projKey] = projDateMap;
            }
            var existingProjDate = projDateMap.TryGetValue(pairKey, out var epd) ? epd : null;
            projDateMap[pairKey] = r.LinkChangedDate.HasValue && (existingProjDate == null || r.LinkChangedDate > existingProjDate)
                ? r.LinkChangedDate
                : existingProjDate;

            if (!perProjectPairTypes.TryGetValue(projKey, out var projTypeMap))
            {
                projTypeMap = new Dictionary<ProjectPairKey, Dictionary<string, int>>();
                perProjectPairTypes[projKey] = projTypeMap;
            }
            if (!projTypeMap.TryGetValue(pairKey, out var projTypeCounts))
            {
                projTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                projTypeMap[pairKey] = projTypeCounts;
            }
            projTypeCounts[wiType] = projTypeCounts.TryGetValue(wiType, out var ptc) ? ptc + 1 : 1;

            if (!perOrgPairs.TryGetValue(orgName, out var orgPairMap))
            {
                orgPairMap = new Dictionary<ProjectPairKey, int>();
                perOrgPairs[orgName] = orgPairMap;
            }
            orgPairMap[pairKey] = orgPairMap.TryGetValue(pairKey, out var oc) ? oc + 1 : 1;

            if (!perOrgPairDates.TryGetValue(orgName, out var orgDateMap))
            {
                orgDateMap = new Dictionary<ProjectPairKey, DateTimeOffset?>();
                perOrgPairDates[orgName] = orgDateMap;
            }
            var existingOrgDate = orgDateMap.TryGetValue(pairKey, out var eod) ? eod : null;
            orgDateMap[pairKey] = r.LinkChangedDate.HasValue && (existingOrgDate == null || r.LinkChangedDate > existingOrgDate)
                ? r.LinkChangedDate
                : existingOrgDate;

            if (!perOrgPairTypes.TryGetValue(orgName, out var orgTypeMap))
            {
                orgTypeMap = new Dictionary<ProjectPairKey, Dictionary<string, int>>();
                perOrgPairTypes[orgName] = orgTypeMap;
            }
            if (!orgTypeMap.TryGetValue(pairKey, out var orgTypeCounts))
            {
                orgTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                orgTypeMap[pairKey] = orgTypeCounts;
            }
            orgTypeCounts[wiType] = orgTypeCounts.TryGetValue(wiType, out var otc) ? otc + 1 : 1;

            // Track active (InProgress) counts per project-pair and org-pair.
            var isActive = string.Equals(r.SourceWorkItemStateCategory, "InProgress", StringComparison.OrdinalIgnoreCase);

            if (!perProjectPairActive.TryGetValue(projKey, out var projActiveMap))
            {
                projActiveMap = new Dictionary<ProjectPairKey, int>();
                perProjectPairActive[projKey] = projActiveMap;
            }
            if (isActive)
                projActiveMap[pairKey] = projActiveMap.TryGetValue(pairKey, out var pac) ? pac + 1 : 1;
            else if (!projActiveMap.ContainsKey(pairKey))
                projActiveMap[pairKey] = 0;

            if (!perOrgPairActive.TryGetValue(orgName, out var orgActiveMap))
            {
                orgActiveMap = new Dictionary<ProjectPairKey, int>();
                perOrgPairActive[orgName] = orgActiveMap;
            }
            if (isActive)
                orgActiveMap[pairKey] = orgActiveMap.TryGetValue(pairKey, out var oac) ? oac + 1 : 1;
            else if (!orgActiveMap.ContainsKey(pairKey))
                orgActiveMap[pairKey] = 0;
        }

        // ── Per-project output ───────────────────────────────────────────────
        foreach (var ((orgName, project), projRecords) in perProjectRecords)
        {
            var projDir = Path.Combine(outputPath, orgName, project);
            Directory.CreateDirectory(projDir);

            var projDepsCsv = Path.Combine(projDir, "dependencies.csv");
            using (var w = new StreamWriter(projDepsCsv, false, new UTF8Encoding(false)))
            {
                w.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate");
                foreach (var r in projRecords)
                    w.WriteLine(
                        $"{r.SourceWorkItemId},{CsvEscape(r.SourceWorkItemType ?? "")},{CsvEscape(r.SourceProject ?? "")}," +
                        $"{CsvEscape(r.LinkType ?? "")},{r.LinkScope},{r.TargetWorkItemId}," +
                        $"{CsvEscape(r.TargetProject ?? "")},{CsvEscape(r.TargetOrganisation ?? "")},{r.TargetStatus}," +
                        $"{(r.LinkChangedDate.HasValue ? r.LinkChangedDate.Value.ToString("O") : "")}");
            }

            var projDateMap = perProjectPairDates.TryGetValue((orgName, project), out var pdm) ? pdm : null;
            var projTypeMap = perProjectPairTypes.TryGetValue((orgName, project), out var ptm) ? ptm : null;
            var projActiveMap2 = perProjectPairActive.TryGetValue((orgName, project), out var pam) ? pam : null;
            var projPairs = perProjectPairs[(orgName, project)]
                .Select(kvp =>
                {
                    var rec = new ProjectDependencyRecord(kvp.Key, kvp.Value);
                    if (projDateMap != null && projDateMap.TryGetValue(kvp.Key, out var d))
                        rec.MostRecentLinkDate = d;
                    if (projTypeMap != null && projTypeMap.TryGetValue(kvp.Key, out var t))
                        foreach (var (type, count) in t)
                            rec.LinkCountByType[type] = count;
                    if (projActiveMap2 != null && projActiveMap2.TryGetValue(kvp.Key, out var a))
                        rec.ActiveLinkCount = a;
                    return rec;
                })
                .OrderByDescending(p => p.LinkCount)
                .ToList();

            var allProjTypes = projPairs
                .SelectMany(p => p.LinkCountByType.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var groupedCsv = Path.Combine(projDir, "grouped.csv");
            using (var w = new StreamWriter(groupedCsv, false, new UTF8Encoding(false)))
            {
                var typeHeaders = allProjTypes.Count > 0
                    ? "," + string.Join(",", allProjTypes.Select(CsvEscape))
                    : "";
                w.WriteLine($"SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,ActiveLinkCount,MostRecentLinkDate{typeHeaders}");
                foreach (var pair in projPairs)
                {
                    var typeCols = allProjTypes.Count > 0
                        ? "," + string.Join(",", allProjTypes.Select(t => pair.LinkCountByType.TryGetValue(t, out var c) ? c.ToString() : "0"))
                        : "";
                    w.WriteLine(
                        $"{CsvEscape(pair.SourceProject)},{CsvEscape(pair.TargetProject)},{CsvEscape(pair.TargetOrganisation)},{pair.LinkCount},{pair.LinkScope},{pair.ActiveLinkCount}," +
                        $"{(pair.MostRecentLinkDate.HasValue ? pair.MostRecentLinkDate.Value.ToString("O") : "")}{typeCols}");
                }
            }

            File.WriteAllText(
                Path.Combine(projDir, "dependencies.md"),
                new MermaidDiagramBuilder(projPairs).Build());
        }

        // ── Per-org output ───────────────────────────────────────────────
        foreach (var (orgName, orgPairMap) in perOrgPairs)
        {
            var orgDir = Path.Combine(outputPath, orgName);
            Directory.CreateDirectory(orgDir);

            var orgDateMap = perOrgPairDates.TryGetValue(orgName, out var odm) ? odm : null;
            var orgTypeMap = perOrgPairTypes.TryGetValue(orgName, out var otm) ? otm : null;
            var orgActiveMap2 = perOrgPairActive.TryGetValue(orgName, out var oam) ? oam : null;
            var orgPairs = orgPairMap
                .Select(kvp =>
                {
                    var rec = new ProjectDependencyRecord(kvp.Key, kvp.Value);
                    if (orgDateMap != null && orgDateMap.TryGetValue(kvp.Key, out var d))
                        rec.MostRecentLinkDate = d;
                    if (orgTypeMap != null && orgTypeMap.TryGetValue(kvp.Key, out var t))
                        foreach (var (type, count) in t)
                            rec.LinkCountByType[type] = count;
                    if (orgActiveMap2 != null && orgActiveMap2.TryGetValue(kvp.Key, out var a))
                        rec.ActiveLinkCount = a;
                    return rec;
                })
                .OrderByDescending(p => p.LinkCount)
                .ToList();

            var componentIds = UnionFindComponentLabeler.AssignComponentIds(orgPairs);
            foreach (var pair in orgPairs)
            {
                if (componentIds.TryGetValue(pair.SourceProject, out var gid))
                    pair.GroupId = gid;
            }

            var allOrgTypes = orgPairs
                .SelectMany(p => p.LinkCountByType.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var orgDepsCsv = Path.Combine(orgDir, "dependencies.csv");
            using (var w = new StreamWriter(orgDepsCsv, false, new UTF8Encoding(false)))
            {
                var typeHeaders = allOrgTypes.Count > 0
                    ? "," + string.Join(",", allOrgTypes.Select(CsvEscape))
                    : "";
                w.WriteLine($"SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId,ActiveLinkCount,MostRecentLinkDate{typeHeaders}");
                foreach (var pair in orgPairs)
                {
                    var typeCols = allOrgTypes.Count > 0
                        ? "," + string.Join(",", allOrgTypes.Select(t => pair.LinkCountByType.TryGetValue(t, out var c) ? c.ToString() : "0"))
                        : "";
                    w.WriteLine(
                        $"{CsvEscape(pair.SourceProject)},{CsvEscape(pair.TargetProject)},{CsvEscape(pair.TargetOrganisation)},{pair.LinkCount},{pair.LinkScope},{pair.GroupId},{pair.ActiveLinkCount}," +
                        $"{(pair.MostRecentLinkDate.HasValue ? pair.MostRecentLinkDate.Value.ToString("O") : "")}{typeCols}");
                }
            }

            File.WriteAllText(
                Path.Combine(orgDir, "dependencies.md"),
                new MermaidDiagramBuilder(orgPairs).Build());
        }

        // ── Console summary ──────────────────────────────────────────────
        var totalLinks = crossProjectCount + crossOrgCount;

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
        summaryTable.AddRow("Output Directory", Markup.Escape(outputPath));
        console.Write(summaryTable);

        if (crossOrgCount > 0)
            console.MarkupLine($"[red]⚠ ACTION REQUIRED: {crossOrgCount} cross-organisation link(s) will break after migration[/]");

        var allPairs = perOrgPairs.Keys
            .SelectMany(orgName =>
            {
                var odm = perOrgPairDates.TryGetValue(orgName, out var dm) ? dm : null;
                var otm = perOrgPairTypes.TryGetValue(orgName, out var tm) ? tm : null;
                var oam = perOrgPairActive.TryGetValue(orgName, out var am) ? am : null;
                return perOrgPairs[orgName].Select(kvp =>
                {
                    var rec = new ProjectDependencyRecord(kvp.Key, kvp.Value);
                    if (odm != null && odm.TryGetValue(kvp.Key, out var d))
                        rec.MostRecentLinkDate = d;
                    if (otm != null && otm.TryGetValue(kvp.Key, out var t))
                        foreach (var (type, count) in t)
                            rec.LinkCountByType[type] = count;
                    if (oam != null && oam.TryGetValue(kvp.Key, out var a))
                        rec.ActiveLinkCount = a;
                    return rec;
                });
            })
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
                .AddColumn(new TableColumn("Active").RightAligned())
                .AddColumn("Scope")
                .AddColumn("Most Recent")
                .AddColumn("By Type");

            foreach (var pair in allPairs)
            {
                var targetDisplay = pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation)
                    ? $"🌐 {Markup.Escape(pair.TargetOrganisation)}/{Markup.Escape(pair.TargetProject ?? "")}"
                    : Markup.Escape(pair.TargetProject ?? "");
                var scopeDisplay = pair.LinkScope == LinkScope.CrossOrganisation
                    ? "[red]Cross-Org[/]"
                    : "Cross-Project";
                var dateDisplay = pair.MostRecentLinkDate.HasValue
                    ? pair.MostRecentLinkDate.Value.ToLocalTime().ToString("yyyy-MM-dd")
                    : "[grey]-[/]";
                var typeDisplay = pair.LinkCountByType.Count > 0
                    ? string.Join(", ", pair.LinkCountByType
                        .OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{Markup.Escape(kv.Key)}:{kv.Value}"))
                    : "[grey]-[/]";
                var activeDisplay = pair.ActiveLinkCount > 0
                    ? $"[yellow]{pair.ActiveLinkCount}[/]"
                    : "[grey]0[/]";
                projectTable.AddRow(
                    Markup.Escape(pair.SourceProject),
                    targetDisplay,
                    pair.LinkCount.ToString(),
                    activeDisplay,
                    scopeDisplay,
                    dateDisplay,
                    typeDisplay);
            }

            console.Write(projectTable);
            console.MarkupLine($"[green]✓[/] Project dependencies written to [blue]{Markup.Escape(outputPath)}[/]");
            console.MarkupLine($"[green]✓[/] Dependency diagram written to [blue]{Markup.Escape(outputPath)}[/]");
        }

        console.MarkupLine($"\n[green]✓[/] Dependency discovery completed successfully.");
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

        progress.WorkItemsAnalysed = evt.TotalWorkItems;
        progress.ExternalLinks = evt.WorkItemsProcessed;
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

    /// <summary>
    /// Builds the live display content: the progress table plus a throughput stats panel.
    /// </summary>
    private static IRenderable BuildLivePanel(IEnumerable<ProjectProgress> state, DateTimeOffset startTime)
    {
        var table = BuildProgressTable(state);
        var stats = BuildThroughputPanel(state, startTime);
        return stats is not null ? new Rows(table, stats) : table;
    }

    /// <summary>
    /// Builds a throughput stats panel showing rates, elapsed time, and ETA.
    /// </summary>
    private static IRenderable? BuildThroughputPanel(IEnumerable<ProjectProgress> state, DateTimeOffset startTime)
    {
        var elapsed = DateTimeOffset.UtcNow - startTime;
        if (elapsed.TotalSeconds < 1)
            return null;

        int completed = 0, inProgress = 0;
        long totalAnalysed = 0, totalLinks = 0;
        foreach (var p in state)
        {
            if (p.IsComplete) completed++;
            else if (p.WorkItemsAnalysed > 0) inProgress++;
            totalAnalysed += p.WorkItemsAnalysed;
            totalLinks += p.ExternalLinks;
        }

        var hours = elapsed.TotalHours;
        var analysedPerHour = hours > 0.001 ? totalAnalysed / hours : 0;
        var linksPerHour = hours > 0.001 ? totalLinks / hours : 0;
        var projPerHour = hours > 0.001 ? completed / hours : 0;

        var statsTable = new Table()
            .NoBorder()
            .HideHeaders()
            .AddColumn(new TableColumn("Label").NoWrap())
            .AddColumn(new TableColumn("Value").RightAligned());

        statsTable.AddRow("[dim]Elapsed[/]", $"[white]{FormatTimeSpan(elapsed)}[/]");
        if (completed > 0)
        {
            statsTable.AddRow("[dim]Projects / hour[/]", $"[white]{projPerHour:N1}[/]");
            statsTable.AddRow("[dim]Work Items Analysed / hour[/]", $"[white]{analysedPerHour:N0}[/]");
            statsTable.AddRow("[dim]Links Found / hour[/]", $"[white]{linksPerHour:N0}[/]");

            var avgMs = elapsed.TotalMilliseconds / completed;
            statsTable.AddRow("[dim]Avg Project Duration[/]", $"[white]{FormatTimeSpan(TimeSpan.FromMilliseconds(avgMs))}[/]");

            if (inProgress > 0)
            {
                var eta = TimeSpan.FromMilliseconds(avgMs * inProgress);
                statsTable.AddRow("[dim]ETA (remaining)[/]", $"[yellow]{FormatTimeSpan(eta)}[/]");
            }
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

    /// <summary>
    /// Parses the root <c>dependencies.csv</c> written by <c>DependencyDiscoveryModule</c>
    /// into <see cref="DependencyRecord"/> objects for post-processing.
    /// </summary>
    private static List<DependencyRecord> ParseRootCsv(string path)
    {
        var records = new List<DependencyRecord>();
        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1)
            return records;

        // Skip header.
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = SplitCsvLine(line);
            if (fields.Length < 10)
                continue;

            records.Add(new DependencyRecord
            {
                SourceWorkItemId = int.TryParse(fields[0], out var sid) ? sid : 0,
                SourceWorkItemType = fields[1],
                SourceProject = fields[2],
                SourceOrganisationUrl = fields[3],
                LinkType = fields[4],
                LinkScope = Enum.TryParse<LinkScope>(fields[5], true, out var ls) ? ls : LinkScope.CrossProject,
                TargetWorkItemId = int.TryParse(fields[6], out var tid) ? tid : 0,
                TargetProject = fields[7],
                TargetOrganisation = fields[8],
                TargetStatus = Enum.TryParse<TargetStatus>(fields[9], true, out var ts) ? ts : TargetStatus.Unknown,
                LinkChangedDate = fields.Length > 10 && DateTimeOffset.TryParse(fields[10], out var d) ? d : (DateTimeOffset?)null,
                SourceWorkItemStateCategory = fields.Length > 11 ? fields[11] : null
            });
        }

        return records;
    }

    /// <summary>
    /// Splits a CSV line respecting quoted fields.
    /// </summary>
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
        var clean = Regex.Replace(name, @"[^\w\-]", "_");
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private sealed class ProjectProgress
    {
        public string OrgName { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public int WorkItemsAnalysed { get; set; }
        public int ExternalLinks { get; set; }
        public int CrossProjectLinks { get; set; }
        public int CrossOrgLinks { get; set; }
        public bool IsComplete { get; set; }
        public string? Error { get; set; }
    }
}
