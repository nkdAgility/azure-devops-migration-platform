// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
#endif
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates team export, import, and validation operations.
/// Handles the enumeration loop, checkpointing, progress events, and metrics — delegates
/// per-team operations to <see cref="TeamExportOrchestrator"/> and, on net10, <see cref="TeamImportOrchestrator"/>.
/// </summary>
internal sealed class TeamsOrchestrator : ITeamsOrchestrator
{
    private const string ModuleName = "Teams";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
    private static readonly ActivitySource s_discoveryActivitySource = new(WellKnownActivitySourceNames.Discovery);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly TeamExportOrchestrator? _exportOrchestrator;
#if !NET481
    private readonly TeamImportOrchestrator? _importOrchestrator;
#endif
    private readonly TeamSlugGenerator? _slugGenerator;
    private readonly IPackageAccess? _package;
    private readonly IReadOnlyList<IModuleExtension> _exportExtensions;
    private readonly IReadOnlyList<IModuleExtension> _importExtensions;

    public TeamsOrchestrator(
        ILogger<TeamsOrchestrator> logger,
        IPlatformMetrics? PlatformMetrics = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamSlugGenerator? slugGenerator = null,
        IPackageAccess? package = null,
        IEnumerable<IModuleExtension>? extensions = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _PlatformMetrics = PlatformMetrics;
        _exportOrchestrator = exportOrchestrator;
        _slugGenerator = slugGenerator;
        _package = package;

        var allExtensions = (extensions ?? Enumerable.Empty<IModuleExtension>())
            .Where(e => string.Equals(e.Module, ModuleName, StringComparison.Ordinal) && e.IsEnabled)
            .OrderBy(e => e.Order)
            .ToList();

        _exportExtensions = allExtensions.Where(e => e.SupportsExport).ToList().AsReadOnly();
        _importExtensions = allExtensions.Where(e => e.SupportsImport).ToList().AsReadOnly();
    }

#if !NET481
    public TeamsOrchestrator(
        ILogger<TeamsOrchestrator> logger,
        IPlatformMetrics? PlatformMetrics = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null,
        TeamSlugGenerator? slugGenerator = null,
        IPackageAccess? package = null,
        IEnumerable<IModuleExtension>? extensions = null)
        : this(logger, PlatformMetrics, exportOrchestrator, slugGenerator, package, extensions)
    {
        _importOrchestrator = importOrchestrator;
    }
#endif

    /// <summary>
    /// Inventory phase: enumerates teams and merges the count into the project
    /// inventory file. Owns the enumeration loop, progress events, and metrics.
    /// </summary>
    public async Task<TaskExecutionResult> CaptureAsync(
        ITeamSource? teamSource,
        InventoryContext context,
        string fallbackOrgUrl,
        CancellationToken ct)
    {
        using var activity = s_discoveryActivitySource.StartActivity("inventory.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", ModuleName);
        activity?.SetTag("org", context.SourceEndpoint?.ResolvedUrl ?? string.Empty);
        activity?.SetTag("project", context.Project);

        if (string.IsNullOrWhiteSpace(context.Project))
        {
            _logger.LogError("[Teams] CaptureAsync called with empty Project — executor contract violated. Skipping.");
            return TaskExecutionResult.Skipped("CaptureAsync called with empty project.");
        }

        _logger.LogInformation("Inventorying {Module}", ModuleName);
        context.ProgressSink?.Emit(new ProgressEvent { Module = ModuleName, Stage = "Inventorying", Message = $"Inventorying {ModuleName}", Timestamp = DateTimeOffset.UtcNow });

        var count = 0;
        if (teamSource is not null)
        {
            var project = context.Project;
            var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? fallbackOrgUrl;
            var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(orgUrl);

            try
            {
                await foreach (var _ in teamSource.EnumerateTeamsAsync(project, ct).ConfigureAwait(false))
                    count++;

                await ProjectInventoryFile.MergeAsync(
                    context.Package, orgSlug, project,
                    orgUrl: orgUrl,
                    teams: count, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                using (_logger.BeginDataScope(DataClassification.Customer))
                    _logger.LogWarning(ex, "Failed to enumerate teams for project {Project}; skipping.", project);
            }
        }

        _PlatformMetrics?.RecordInventoryTeams(count, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", ModuleName } });
        _logger.LogInformation("Inventoried {Module}: {Count} items", ModuleName, count);
        if (count == 0)
            _logger.LogWarning("Zero items inventoried for {Module}", ModuleName);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Inventoried",
            Message = $"{ModuleName} inventory complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Discovery = new DiscoveryCounters
                {
                    Inventory = new InventoryCounters { RevisionsTotal = count }
                }
            }
        });

        return TaskExecutionResult.Completed();
    }

    /// <summary>
    /// Prepare phase: generates the Teams prepare report, records prepare metrics, and
    /// persists <c>prepare-report.json</c> into the package.
    /// </summary>
    public async Task PrepareAsync(
        PrepareContext context,
        string organisation,
        string project,
        CancellationToken ct)
    {
        var report = new PrepareReport { ModuleName = ModuleName, ResolvedCount = 0 };
        _PlatformMetrics?.RecordPrepareTeamsResolved(report.ResolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", ModuleName } });
        _PlatformMetrics?.RecordPrepareTeamsUnresolved(report.UnresolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", ModuleName } });

        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report)), writable: false))
        {
            await context.Package.PersistContentAsync(
                new PackageContentContext(PackageContentKind.Artefact, Module: ModuleName, Organisation: organisation, Project: project, Address: new RelativePathAddress("prepare-report.json")),
                new PackagePayload(stream, "application/json"),
                ct).ConfigureAwait(false);
        }
        _logger.LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", ModuleName, report.ResolvedCount, report.UnresolvedCount, 0);
    }

    /// <summary>
    /// Exports all teams from the source project: enumerates, filters, writes team.json files
    /// via <paramref name="exportOrchestrator"/>, and writes checkpoint on completion.
    /// </summary>
    public async Task ExportAsync(
        ITeamSource teamSource,
        ExportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct)
    {
        if (_exportOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamExportOrchestrator available — team export skipped.");
            return;
        }

        if (_slugGenerator is null)
        {
            _logger.LogWarning("[Teams] No TeamSlugGenerator available — team export skipped.");
            return;
        }
        using var activity = s_activitySource.StartActivity("teams.export");

        var projectName = sourceEndpointInfo.Project;

        using (_logger.BeginDataScope(DataClassification.Customer))
            _logger.LogInformation("[Teams] Exporting teams for project '{Project}'.", projectName);

        var sink = context.ProgressSink;
        sink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Teams.Export.Started",
            Message = $"Starting team export for project '{projectName}'."
        });

        var checkpointing = checkpointingFactory?.Create(context.Package);

        Regex? filterRegex = null;
        if (string.Equals(options.Scope, "teams", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(options.Filter))
        {
            filterRegex = new Regex(options.Filter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        var count = 0;
        var skipped = 0;
        await foreach (var team in teamSource.EnumerateTeamsAsync(projectName, ct).ConfigureAwait(false))
        {
            if (filterRegex is not null && !filterRegex.IsMatch(team.Name))
            {
                _logger.LogDebug("[Teams] Skipping team '{Name}' — does not match filter '{Filter}'.",
                    team.Name, options.Filter);
                continue;
            }

            var slug = _slugGenerator.GenerateSlug(team.Name);
            var artifactPath = $"Teams/{slug}/team.json";

            if (!options.AlwaysExport
                && await TeamDefinitionExistsAsync(sourceEndpointInfo.OrganisationSlug, sourceEndpointInfo.Project, artifactPath, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("[Teams] Skipping already-exported team '{Name}' ({Path}) — use AlwaysExport: true to force re-export.",
                    team.Name, artifactPath);
                skipped++;
                continue;
            }

            var exportTags = new MetricsTagList
            {
                { "module", "Teams" },
                { "operation", "teams.export" }
            };
            _PlatformMetrics?.IncrementTeamExportInFlight(exportTags);
            var exportSw = Stopwatch.StartNew();
            try
            {
                await _exportOrchestrator.ExportTeamAsync(
                    sourceEndpointInfo.OrganisationSlug, projectName, team, slug, _package!, options.Extensions, ct).ConfigureAwait(false);

                // Dispatch enabled export extensions in order
                if (_exportExtensions.Count > 0)
                {
                    var extensionContext = new TeamExtensionContext
                    {
                        Organisation = sourceEndpointInfo.OrganisationSlug,
                        ProjectName = projectName,
                        EntityId = team.Id,
                        TargetEntityId = null,
                        Package = _package!,
                        Team = team,
                        Slug = slug,
                        SourceProjectName = projectName,
                        ProgressSink = sink
                    };

                    foreach (var ext in _exportExtensions)
                    {
                        try
                        {
                            await ext.ExportAsync(extensionContext, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception extEx)
                        {
                            _logger.LogWarning(extEx,
                                "[Teams] Extension '{ExtName}' export failed for team '{TeamName}' — continuing.",
                                ext.Name, team.Name);
                        }
                    }
                }

                count++;
                _PlatformMetrics?.RecordTeamExportCount(exportTags);
                sink?.Emit(new ProgressEvent
                {
                    Module = ModuleName,
                    Stage = "Teams.Export.Team",
                    Message = $"Exported team '{team.Name}' ({count} total).",
                });
            }
            catch
            {
                _PlatformMetrics?.RecordTeamExportError(exportTags);
                throw;
            }
            finally
            {
                exportSw.Stop();
                _PlatformMetrics?.DecrementTeamExportInFlight(exportTags);
                _PlatformMetrics?.RecordTeamExportDuration(exportSw.Elapsed.TotalMilliseconds, exportTags);
            }
        }

        activity?.SetTag("teams.count", count);
        activity?.SetTag("teams.skipped", skipped);
        _logger.LogInformation("[Teams] Exported {Count} teams ({Skipped} skipped — already present).", count, skipped);
        if (count == 0 && skipped == 0)
            _logger.LogWarning("[Teams] Export completed with zero teams exported and zero skipped — verify the source project has teams.");

        sink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Teams.Export.Complete",
            Message = $"Team export complete — {count} teams exported, {skipped} skipped (already present).",
            Metrics = new JobMetrics
            {
                Migration = new MigrationCounters
                {
                    Teams = new TeamsCounters { Exported = count, Skipped = skipped }
                }
            }
        });

        if (checkpointing is not null)
        {
            await checkpointing.WriteCursorAsync("export.teams", new CursorEntry
            {
                LastProcessed = $"Teams/{count}",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports all team packages from the artefact store: enumerates team.json files,
    /// deserialises, and delegates per-team import to <paramref name="importOrchestrator"/>.
    /// Writes checkpoint on completion.
    /// </summary>
#if !NET481
    public async Task ImportAsync(
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct)
    {
        if (_importOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamImportOrchestrator available — team import skipped.");
            return;
        }
        using var activity = s_activitySource.StartActivity("teams.import");

        var projectName = targetEndpointInfo.Project;
        var sourceProjectName = sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(sourceProjectName))
        {
            sourceProjectName = projectName;
        }
        if (string.IsNullOrWhiteSpace(sourceProjectName))
        {
            sourceProjectName = "unknown";
        }

        var sourceOrganisation = sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(sourceOrganisation))
        {
            sourceOrganisation = targetEndpointInfo.OrganisationSlug;
        }
        if (string.IsNullOrWhiteSpace(sourceOrganisation))
        {
            sourceOrganisation = "unknown";
        }

        using (_logger.BeginDataScope(DataClassification.Customer))
            _logger.LogInformation("[Teams] Importing teams for project '{Project}'.", projectName);

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Teams.Import.Started",
            Message = $"Starting team import for project '{projectName}'.",
        });

        var count = 0;
        await foreach (var teamPath in EnumeratePackageContentAsync(context.Package, sourceOrganisation, sourceProjectName, ct).ConfigureAwait(false))
        {
            if (!teamPath.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var json = await ReadPackageContentAsync(context.Package, sourceOrganisation, sourceProjectName, teamPath, ct).ConfigureAwait(false);
            if (json is null)
            {
                _logger.LogWarning("[Teams] Could not read team file '{Path}' — skipping.", teamPath);
                continue;
            }

            TeamPackage? teamPackage;
            try
            {
                teamPackage = JsonSerializer.Deserialize<TeamPackage>(json, s_jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "[Teams] Malformed JSON in '{Path}' — skipping.", teamPath);
                continue;
            }

            if (teamPackage is null)
            {
                _logger.LogWarning("[Teams] Null team package in '{Path}' — skipping.", teamPath);
                continue;
            }

            var importTags = new MetricsTagList
            {
                { "module", "Teams" },
                { "operation", "teams.import" }
            };
            _PlatformMetrics?.IncrementTeamImportInFlight(importTags);
            var importSw = Stopwatch.StartNew();
            try
            {
                var targetTeamId = await _importOrchestrator.ImportTeamAsync(
                    projectName, sourceProjectName, teamPackage, options.Extensions, ct).ConfigureAwait(false);

                // Package upgrader (T101): if split artifact files are absent but team.json
                // contains legacy embedded capability data, write the split files so that
                // extensions can read them. This ensures old-format packages import correctly.
                if (_importExtensions.Count > 0 && teamPackage.Definition is not null)
                {
                    var slug = GetTeamSlug(teamPath);
                    await UpgradeLegacyTeamPackageAsync(
                        context.Package, teamPackage, sourceOrganisation, sourceProjectName, slug, ct).ConfigureAwait(false);
                }

                // Dispatch enabled import extensions in order
                if (_importExtensions.Count > 0 && teamPackage.Definition is not null)
                {
                    var slug = GetTeamSlug(teamPath);
                    var extensionContext = new TeamExtensionContext
                    {
                        Organisation = sourceOrganisation,
                        ProjectName = projectName,
                        EntityId = teamPackage.Definition.Id,
                        TargetEntityId = targetTeamId,
                        Package = context.Package,
                        Team = teamPackage.Definition,
                        Slug = slug,
                        SourceProjectName = sourceProjectName,
                        ProgressSink = importSink
                    };

                    foreach (var ext in _importExtensions)
                    {
                        try
                        {
                            await ext.ImportAsync(extensionContext, ct).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception extEx)
                        {
                            _logger.LogWarning(extEx,
                                "[Teams] Extension '{ExtName}' import failed for team '{TeamName}' — continuing.",
                                ext.Name, teamPackage.Definition.Name);
                        }
                    }
                }

                count++;
                _PlatformMetrics?.RecordTeamImportCount(importTags);
                importSink?.Emit(new ProgressEvent
                {
                    Module = ModuleName,
                    Stage = "Teams.Import.Team",
                    Message = $"Imported team '{teamPackage.Definition?.Name}' ({count} total).",
                });
            }
            catch
            {
                _PlatformMetrics?.RecordTeamImportError(importTags);
                throw;
            }
            finally
            {
                importSw.Stop();
                _PlatformMetrics?.DecrementTeamImportInFlight(importTags);
                _PlatformMetrics?.RecordTeamImportDuration(importSw.Elapsed.TotalMilliseconds, importTags);
            }
        }

        activity?.SetTag("teams.count", count);
        _logger.LogInformation("[Teams] Imported {Count} teams.", count);
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Teams.Import.Complete",
            Message = $"Team import complete — {count} teams imported.",
        });

        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(context.Package);
            await checkpointing.WriteCursorAsync("import.teams", new CursorEntry
            {
                LastProcessed = $"Teams/{count}",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }
#endif

    /// <summary>
    /// Validates that team.json files exist under Teams/ and are well-formed with a
    /// <c>definition</c> field.
    /// </summary>
    public async Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct)
    {
        var teamCount = 0;
        var validateTags = new MetricsTagList
        {
            { "module", "Teams" },
            { "operation", "teams.validate" }
        };

        await foreach (var teamPath in EnumeratePackageContentAsync(package, organisation, project, ct).ConfigureAwait(false))
        {
            if (!teamPath.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase))
                continue;

            teamCount++;

            var json = await ReadPackageContentAsync(package, organisation, project, teamPath, ct).ConfigureAwait(false);
            if (json is null)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = teamPath,
                    Message = $"[Teams] Team file '{teamPath}' exists but could not be read."
                });
                _PlatformMetrics?.RecordTeamValidateError(validateTags);
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("definition", out _))
                {
                    context.Errors.Add(new ValidationError
                    {
                        Path = teamPath,
                        Message = $"[Teams] Team file '{teamPath}' is missing required field 'definition'."
                    });
                    _PlatformMetrics?.RecordTeamValidateError(validateTags);
                }
                else
                {
                    _PlatformMetrics?.RecordTeamValidateCount(validateTags);
                }
            }
            catch (JsonException ex)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = teamPath,
                    Message = $"[Teams] Team file '{teamPath}' contains malformed JSON: {ex.Message}"
                });
                _PlatformMetrics?.RecordTeamValidateError(validateTags);
            }
        }

        if (teamCount == 0)
        {
            context.Errors.Add(new ValidationError
            {
                Path = "Teams/",
                Message = "[Teams] No team files found in package under 'Teams/'."
            });
        }
    }

    private static async IAsyncEnumerable<string> EnumeratePackageContentAsync(
        IPackageAccess package,
        string organisation,
        string project,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var paths = package.EnumerateContentAsync(
            new PackageContentContext(
                PackageContentKind.Collection,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                IsCollectionRequest: true),
            ct);
        if (paths is null)
            yield break;

        await foreach (var path in paths.ConfigureAwait(false))
            yield return path;
    }

    private static async Task<string?> ReadPackageContentAsync(IPackageAccess package, string organisation, string project, string relativePath, CancellationToken ct)
    {
        var payload = await package.RequestContentAsync(
            CreateTeamContext(organisation, project, relativePath),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, true, 1024, false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task<bool> TeamDefinitionExistsAsync(string organisation, string project, string relativePath, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        return await _package.ContentExistsAsync(
            CreateTeamContext(organisation, project, relativePath),
            ct).ConfigureAwait(false);
    }

    private static PackageContentContext CreateTeamContext(string organisation, string project, string relativePath)
        => new(
            PackageContentKind.Artefact,
            Organisation: organisation,
            Project: project,
            Module: "Teams",
            Address: new TeamDefinitionAddress(GetTeamSlug(relativePath)));

    private static string GetTeamSlug(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (normalized.StartsWith("Teams/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("Teams/".Length);

        var suffix = "/team.json";
        return normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - suffix.Length)
            : normalized;
    }

    /// <summary>
    /// Package upgrader (T101 — Constitution VII): if split artifact files are absent but
    /// <paramref name="teamPackage"/> contains legacy embedded capability data (old format
    /// where all data lived in <c>team.json</c>), writes split artifact files so that
    /// <see cref="IModuleExtension"/> implementations can read them.
    /// </summary>
    private static async Task UpgradeLegacyTeamPackageAsync(
        IPackageAccess package,
        TeamPackage teamPackage,
        string organisation,
        string project,
        string slug,
        CancellationToken ct)
    {
        // Upgrade iterations: if team.json has iteration data but iterations.json is absent, write it
        if (teamPackage.Iterations is { Count: > 0 })
        {
            var iterCtx = new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "iterations.json"));

            if (!await package.ContentExistsAsync(iterCtx, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(teamPackage.Iterations, LegacyJsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                await package.PersistContentAsync(iterCtx, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
            }
        }

        // Upgrade members: if team.json has member data but members.json is absent, write it
        if (teamPackage.Members is { Count: > 0 })
        {
            var memberCtx = new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "members.json"));

            if (!await package.ContentExistsAsync(memberCtx, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(teamPackage.Members, LegacyJsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                await package.PersistContentAsync(memberCtx, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
            }
        }

        // Upgrade capacity: if team.json has capacity data but capacity.json is absent, write it
        // Also requires iterations.json to exist (the capacity extension reads it to get iteration IDs)
        if (teamPackage.CapacityByIteration is { Count: > 0 })
        {
            var capacityCtx = new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "capacity.json"));

            if (!await package.ContentExistsAsync(capacityCtx, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(teamPackage.CapacityByIteration, LegacyJsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                await package.PersistContentAsync(capacityCtx, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
            }
        }

        // Upgrade settings: if team.json has settings data but settings.json is absent, write it
        if (teamPackage.Settings is not null)
        {
            var settingsCtx = new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "settings.json"));

            if (!await package.ContentExistsAsync(settingsCtx, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(teamPackage.Settings, LegacyJsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                await package.PersistContentAsync(settingsCtx, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
            }
        }

        // Upgrade area paths: if team.json has area path data but area-paths.json is absent, write it
        if (teamPackage.AreaPaths is not null)
        {
            var areaPathsCtx = new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "area-paths.json"));

            if (!await package.ContentExistsAsync(areaPathsCtx, ct).ConfigureAwait(false))
            {
                var json = JsonSerializer.Serialize(teamPackage.AreaPaths, LegacyJsonOptions);
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
                await package.PersistContentAsync(areaPathsCtx, new PackagePayload(stream, "application/json"), ct).ConfigureAwait(false);
            }
        }
    }

    private static readonly JsonSerializerOptions LegacyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}
