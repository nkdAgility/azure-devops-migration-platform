// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates team export, import, and validation operations.
/// Handles the enumeration loop, checkpointing, progress events, and metrics — delegates
/// per-team operations to <see cref="TeamExportOrchestrator"/> and <see cref="TeamImportOrchestrator"/>.
/// </summary>
internal sealed class TeamsOrchestrator : ITeamsOrchestrator
{
    private const string ModuleName = "Teams";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly TeamExportOrchestrator? _exportOrchestrator;
    private readonly TeamImportOrchestrator? _importOrchestrator;
    private readonly TeamSlugGenerator? _slugGenerator;
    private readonly IPackage? _package;

    public TeamsOrchestrator(
        ILogger<TeamsOrchestrator> logger,
        IPlatformMetrics? PlatformMetrics = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null,
        TeamSlugGenerator? slugGenerator = null,
        IPackage? package = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _PlatformMetrics = PlatformMetrics;
        _exportOrchestrator = exportOrchestrator;
        _importOrchestrator = importOrchestrator;
        _slugGenerator = slugGenerator;
        _package = package;
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

        var artefactStore = context.ArtefactStore;
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

        var checkpointing = checkpointingFactory?.Create(context.StateStore);

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
                && await TeamDefinitionExistsAsync(artefactStore, artifactPath, ct).ConfigureAwait(false))
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
                    projectName, team, slug, artefactStore, options.Extensions, ct).ConfigureAwait(false);

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

        var artefactStore = context.ArtefactStore;
        var projectName = targetEndpointInfo.Project;
        var sourceProjectName = sourceEndpointInfo.Project;

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
        await foreach (var teamPath in artefactStore.EnumerateAsync("Teams/", ct).ConfigureAwait(false))
        {
            if (!teamPath.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase))
                continue;

            var json = await ReadPackageContentAsync(artefactStore, teamPath, ct).ConfigureAwait(false);
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
                await _importOrchestrator.ImportTeamAsync(
                    projectName, sourceProjectName, teamPackage, options.Extensions, ct).ConfigureAwait(false);

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
            var checkpointing = checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync("import.teams", new CursorEntry
            {
                LastProcessed = $"Teams/{count}",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Validates that team.json files exist under Teams/ and are well-formed with a
    /// <c>definition</c> field.
    /// </summary>
    public async Task ValidateAsync(
        IArtefactStore artefactStore,
        ValidationContext context,
        CancellationToken ct)
    {
        var teamCount = 0;
        var validateTags = new MetricsTagList
        {
            { "module", "Teams" },
            { "operation", "teams.validate" }
        };

        await foreach (var teamPath in artefactStore.EnumerateAsync("Teams/", ct).ConfigureAwait(false))
        {
            if (!teamPath.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase))
                continue;

            teamCount++;

            var json = await ReadPackageContentAsync(artefactStore, teamPath, ct).ConfigureAwait(false);
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

    private async Task<string?> ReadPackageContentAsync(IArtefactStore artefactStore, string relativePath, CancellationToken ct)
    {
        if (_package is not null)
        {
            var payload = await _package.RequestAsync(new PackageContext(relativePath), ct).ConfigureAwait(false);
            if (payload is not null)
            {
                if (payload.Content.CanSeek)
                    payload.Content.Position = 0;

                using var reader = new StreamReader(payload.Content);
                return await reader.ReadToEndAsync().ConfigureAwait(false);
            }
        }

        return await artefactStore.ReadAsync(relativePath, ct).ConfigureAwait(false);
    }

    private async Task<bool> TeamDefinitionExistsAsync(IArtefactStore artefactStore, string relativePath, CancellationToken ct)
    {
        if (_package is not null)
        {
            var payload = await _package.RequestAsync(new PackageContext(relativePath), ct).ConfigureAwait(false);
            return payload is not null;
        }

        return await artefactStore.ExistsAsync(relativePath, ct).ConfigureAwait(false);
    }
}
#endif

