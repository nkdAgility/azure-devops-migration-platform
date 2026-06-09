// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CsvHelper;
using CsvHelper.Configuration;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Config;
using DevOpsMigrationPlatform.Infrastructure.Simulated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Identifies cross-project and cross-organisation work item links and writes
/// a <c>discovery-dependencies.csv</c> report to the configured output path.
///
/// Runs entirely in-process (no control plane required). Supports Simulated sources
/// for testing via a <c>DependencySimulation.LinkMode</c> configuration key.
/// </summary>
public sealed class DependencyCommand : CommandBase<DependencyCommandSettings>
{
    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        DependencyCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddSingleton<IConfigurationService, ConfigurationService>();
            RegisterDependencyServices(services, config);
        });

        var console = GetRequiredService<IAnsiConsole>();
        var config = await LoadConfigurationAsync(settings, cancellationToken);
        if (config is null)
            return 1;

        // ── Resolve output path ──────────────────────────────────────────────
        // Keep the user-supplied value for display so terminal output references the
        // same string the user typed (e.g. "./reports/deps.csv").
        var outputPathDisplay = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? "discovery-dependencies.csv"
            : settings.OutputPath;
        var outputPath = string.IsNullOrWhiteSpace(settings.OutputPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "discovery-dependencies.csv")
            : Path.GetFullPath(settings.OutputPath);

        // Ensure the output directory exists (for custom --output paths).
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        // ── Build organisations list from config ─────────────────────────────
        var organisations = BuildOrganisations(config);
        if (organisations.Count == 0)
        {
            console.MarkupLine("[yellow]⚠[/] No organisations configured. Use the [cyan]Organisations[/] array in the config.");
            return 1;
        }

        // ── Discover dependencies ────────────────────────────────────────────
        var factory = GetRequiredService<IDependencyDiscoveryServiceFactory>();
        var policies = new JobPolicies();
        var discoveryService = factory.Create(organisations, policies);

        var records = new List<DependencyRecord>();

        console.MarkupLine("[blue]ℹ[/] Discovering dependencies…");
        await foreach (var evt in discoveryService.DiscoverDependenciesAsync(
            cancellationToken: cancellationToken))
        {
            if (evt is DependencyFoundEvent found)
                records.Add(found.Record);
        }

        // ── Write CSV ────────────────────────────────────────────────────────
        WriteCsv(outputPath, records);

        // ── Print summary ────────────────────────────────────────────────────
        var crossOrgCount = records.Count(r => r.LinkScope == LinkScope.CrossOrganisation);
        var crossProjectCount = records.Count(r => r.LinkScope == LinkScope.CrossProject);

        if (records.Count == 0)
        {
            console.MarkupLine("[green]✓[/] No external dependencies found.");
        }
        else
        {
            console.MarkupLine($"[green]✓[/] External Links Found: {records.Count}");
            PrintSummaryTable(console, crossProjectCount, crossOrgCount, outputPathDisplay);

            if (crossOrgCount > 0)
            {
                console.MarkupLine("");
                console.MarkupLine($"[yellow]⚠[/] CrossOrganisation links detected: {crossOrgCount}. ACTION REQUIRED — review cross-org dependencies before migrating.");
            }
        }

        console.MarkupLine($"[dim]Report written to: {outputPathDisplay}[/]");
        return 0;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RegisterDependencyServices(
        IServiceCollection services,
        IConfiguration config)
    {
        // Detect the simulation link mode from the config file (test hook).
        // When the mode is not set, fall through to Simulated defaults.
        var linkMode = config["DependencySimulation:LinkMode"] ?? "external";

        switch (linkMode.ToLowerInvariant())
        {
            case "cross-org":
                services.AddKeyedSingleton<IWorkItemLinkAnalysisService>(
                    "Simulated",
                    (_, _) => new StubCrossOrgLinkAnalysisService());
                services.AddSingleton<IDependencyDiscoveryServiceFactory,
                    Infrastructure.Simulated.Factories.SimulatedDependencyDiscoveryServiceFactory>();
                break;

            case "none":
                // SimulatedWorkItemLinkAnalysisService yields nothing — correct for the no-links scenario.
                services.AddSimulatedDependencyAnalysis();
                break;

            default:
                // "external" — use the simulated service that yields a fixed set of external links.
                services.AddKeyedSingleton<IWorkItemLinkAnalysisService>(
                    "Simulated",
                    (_, _) => new StubExternalLinkAnalysisService());
                services.AddSingleton<IDependencyDiscoveryServiceFactory,
                    Infrastructure.Simulated.Factories.SimulatedDependencyDiscoveryServiceFactory>();
                break;
        }
    }

    private static List<ScopedOrganisationEndpoint> BuildOrganisations(
        MigrationPlatformOptions config)
    {
        var result = new List<ScopedOrganisationEndpoint>();

        // Mode 2: explicit Organisations array (preferred for dependency commands)
        if (config.Organisations.Count > 0)
        {
            foreach (var org in config.Organisations.Where(o => o.Enabled))
            {
                result.Add(new ScopedOrganisationEndpoint
                {
                    Endpoint = org.ToEndpointOptions(),
                    Projects = org.Projects
                });
            }
            return result;
        }

        // Mode 1: single Source (fallback)
        if (config.Source is not null)
        {
            result.Add(new ScopedOrganisationEndpoint
            {
                Endpoint = config.Source,
                Projects = new List<string>()
            });
        }

        return result;
    }

    private static void WriteCsv(string outputPath, List<DependencyRecord> records)
    {
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };

        using var writer = new StreamWriter(outputPath, append: false);
        using var csv = new CsvWriter(writer, csvConfig);

        csv.WriteField("SourceWorkItemId");
        csv.WriteField("SourceWorkItemType");
        csv.WriteField("SourceProject");
        csv.WriteField("LinkType");
        csv.WriteField("LinkScope");
        csv.WriteField("TargetWorkItemId");
        csv.WriteField("TargetProject");
        csv.WriteField("TargetOrganisation");
        csv.WriteField("TargetStatus");
        csv.NextRecord();

        foreach (var r in records)
        {
            csv.WriteField(r.SourceWorkItemId);
            csv.WriteField(r.SourceWorkItemType ?? string.Empty);
            csv.WriteField(r.SourceProject ?? string.Empty);
            csv.WriteField(r.LinkType ?? string.Empty);
            csv.WriteField(r.LinkScope.ToString());
            csv.WriteField(r.TargetWorkItemId);
            csv.WriteField(r.TargetProject ?? string.Empty);
            csv.WriteField(r.TargetOrganisation ?? string.Empty);
            csv.WriteField(r.TargetStatus.ToString());
            csv.NextRecord();
        }
    }

    private static void PrintSummaryTable(
        IAnsiConsole console,
        int crossProjectCount,
        int crossOrgCount,
        string outputPath)
    {
        var table = new Table();
        table.AddColumn("Scope");
        table.AddColumn("Count");

        table.AddRow("CrossProject", crossProjectCount.ToString());

        var crossOrgDisplay = crossOrgCount > 0
            ? $"⚠ {crossOrgCount}"
            : crossOrgCount.ToString();
        table.AddRow("CrossOrganisation", crossOrgDisplay);

        console.Write(table);
        console.MarkupLine($"[dim]Output: {outputPath}[/]");
    }

    // ── Inline test stubs ────────────────────────────────────────────────────

    /// <summary>
    /// Stub used when <c>DependencySimulation:LinkMode = external</c>.
    /// Yields one cross-project link so the default scenario has data rows.
    /// </summary>
    private sealed class StubExternalLinkAnalysisService : IWorkItemLinkAnalysisService
    {
        public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
            MigrationEndpointOptions endpoint,
            string project,
            string? wiqlFilter = null,
            BatchContinuationToken? savedContinuationToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield return new DependencyFoundEvent(new DependencyRecord
            {
                SourceWorkItemId = 1,
                SourceWorkItemType = "User Story",
                SourceProject = project,
                LinkType = "Related",
                LinkScope = LinkScope.CrossProject,
                TargetWorkItemId = 42,
                TargetProject = "OtherProject",
                TargetOrganisation = string.Empty,
                TargetStatus = TargetStatus.Reachable
            });
        }
    }

    /// <summary>
    /// Stub used when <c>DependencySimulation:LinkMode = cross-org</c>.
    /// Yields one cross-organisation link so the warning path is exercised.
    /// </summary>
    private sealed class StubCrossOrgLinkAnalysisService : IWorkItemLinkAnalysisService
    {
        public async IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(
            MigrationEndpointOptions endpoint,
            string project,
            string? wiqlFilter = null,
            BatchContinuationToken? savedContinuationToken = null,
            Func<BatchContinuationToken, CancellationToken, Task>? continuationCheckpointWriter = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.CompletedTask;
            yield return new DependencyFoundEvent(new DependencyRecord
            {
                SourceWorkItemId = 1,
                SourceWorkItemType = "Bug",
                SourceProject = project,
                LinkType = "Related",
                LinkScope = LinkScope.CrossOrganisation,
                TargetWorkItemId = 99,
                TargetProject = "RemoteProject",
                TargetOrganisation = "https://dev.azure.com/other-org",
                TargetStatus = TargetStatus.Reachable
            });
        }
    }
}
