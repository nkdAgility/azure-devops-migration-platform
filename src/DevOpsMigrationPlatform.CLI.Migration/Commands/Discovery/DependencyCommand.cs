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

        Guid jobId;
        try
        {
            jobId = await client.SubmitDiscoveryAsync(job, cancellationToken);
            console.MarkupLine($"[green]✓[/] Discovery job [bold]{jobId}[/] submitted.");
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

        // Follow progress and render a live table (interactive) or line-by-line output (non-interactive).
        var progressState = new Dictionary<string, ProjectProgress>(StringComparer.OrdinalIgnoreCase);

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
                        await foreach (var evt in client.FollowDiscoveryLogsAsync(jobId, cancellationToken))
                        {
                            if (evt.Stage == "Completed")
                                break;

                            UpdateProgressFromEvent(progressState, evt);
                            ctx.UpdateTarget(BuildProgressTable(progressState.Values));
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
            console.MarkupLine("[yellow]Detached from stream. Discovery job continues running.[/]");
            return 0;
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
        var perOrgPairs = new Dictionary<string, Dictionary<ProjectPairKey, int>>();

        foreach (var r in records)
        {
            var orgName = ExtractOrgName(r.SourceOrganisationUrl);
            var project = r.SourceProject ?? "unknown";
            var projKey = (orgName, project);
            var pairKey = new ProjectPairKey(r);

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

            if (!perOrgPairs.TryGetValue(orgName, out var orgPairMap))
            {
                orgPairMap = new Dictionary<ProjectPairKey, int>();
                perOrgPairs[orgName] = orgPairMap;
            }
            orgPairMap[pairKey] = orgPairMap.TryGetValue(pairKey, out var oc) ? oc + 1 : 1;
        }

        // ── Per-project output ───────────────────────────────────────────────
        foreach (var ((orgName, project), projRecords) in perProjectRecords)
        {
            var projDir = Path.Combine(outputPath, orgName, project);
            Directory.CreateDirectory(projDir);

            var projDepsCsv = Path.Combine(projDir, "dependencies.csv");
            using (var w = new StreamWriter(projDepsCsv, false, new UTF8Encoding(false)))
            {
                w.WriteLine("SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus");
                foreach (var r in projRecords)
                    w.WriteLine(
                        $"{r.SourceWorkItemId},{CsvEscape(r.SourceWorkItemType ?? "")},{CsvEscape(r.SourceProject ?? "")}," +
                        $"{CsvEscape(r.LinkType ?? "")},{r.LinkScope},{r.TargetWorkItemId}," +
                        $"{CsvEscape(r.TargetProject ?? "")},{CsvEscape(r.TargetOrganisation ?? "")},{r.TargetStatus}");
            }

            var projPairs = perProjectPairs[(orgName, project)]
                .Select(kvp => new ProjectDependencyRecord(kvp.Key, kvp.Value))
                .OrderByDescending(p => p.LinkCount)
                .ToList();

            var groupedCsv = Path.Combine(projDir, "grouped.csv");
            using (var w = new StreamWriter(groupedCsv, false, new UTF8Encoding(false)))
            {
                w.WriteLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope");
                foreach (var pair in projPairs)
                    w.WriteLine($"{CsvEscape(pair.SourceProject)},{CsvEscape(pair.TargetProject)},{CsvEscape(pair.TargetOrganisation)},{pair.LinkCount},{pair.LinkScope}");
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
            using (var w = new StreamWriter(orgDepsCsv, false, new UTF8Encoding(false)))
            {
                w.WriteLine("SourceProject,TargetProject,TargetOrganisation,LinkCount,LinkScope,GroupId");
                foreach (var pair in orgPairs)
                    w.WriteLine($"{CsvEscape(pair.SourceProject)},{CsvEscape(pair.TargetProject)},{CsvEscape(pair.TargetOrganisation)},{pair.LinkCount},{pair.LinkScope},{pair.GroupId}");
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

    // ── CSV parsing ───────────────────────────────────────────────────────────

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
                TargetStatus = Enum.TryParse<TargetStatus>(fields[9], true, out var ts) ? ts : TargetStatus.Unknown
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
