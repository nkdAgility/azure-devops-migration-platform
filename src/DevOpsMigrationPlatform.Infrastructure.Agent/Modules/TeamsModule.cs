#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Infrastructure.Agent.Teams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// <see cref="IModule"/> implementation for team export/import.
/// Exports all team definitions, settings, iterations, members, capacity, and area paths
/// to <c>Teams/{slug}/team.json</c> files. Imports them idempotently.
/// </summary>
/// <remarks>
/// <strong>Connector coverage:</strong> Team import is supported for
/// <c>AzureDevOpsServices</c> and <c>Simulated</c> connectors only.
/// TFS (TeamFoundationServer) is a <em>source-only</em> connector — it is always
/// the migration origin, never the destination. No <see cref="ITeamTarget"/>
/// implementation exists for TFS and none is required; <see cref="ITeamTarget"/>
/// is also guarded by <c>#if !NET481</c> and therefore not reachable from the TFS
/// subprocess. This is an explicit architectural decision, not an oversight.
/// </remarks>
public sealed class TeamsModule : IModule
{
    private const string ModuleName = "Teams";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly ITeamSource? _teamSource;
    private readonly ITeamTarget? _teamTarget;
    private readonly TeamExportOrchestrator? _exportOrchestrator;
    private readonly TeamImportOrchestrator? _importOrchestrator;
    private readonly TeamSlugGenerator _slugGenerator;
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly IMigrationMetrics? _migrationMetrics;
    private readonly ILogger<TeamsModule> _logger;
    private readonly TeamsModuleOptions _options;

    public string Name => ModuleName;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public TeamsModule(
        ILogger<TeamsModule> logger,
        IOptions<TeamsModuleOptions> options,
        TeamSlugGenerator slugGenerator,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        TeamExportOrchestrator? exportOrchestrator = null,
        TeamImportOrchestrator? importOrchestrator = null,
        ICheckpointingServiceFactory? checkpointingFactory = null,
        IMigrationMetrics? migrationMetrics = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _slugGenerator = slugGenerator ?? throw new ArgumentNullException(nameof(slugGenerator));
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _exportOrchestrator = exportOrchestrator;
        _importOrchestrator = importOrchestrator;
        _checkpointingFactory = checkpointingFactory;
        _migrationMetrics = migrationMetrics;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping export.");
            return;
        }

        if (_teamSource is null)
        {
            _logger.LogWarning("[Teams] No ITeamSource registered — team export skipped.");
            return;
        }

        if (_exportOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamExportOrchestrator registered — team export skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("teams.export");

        var job = context.Job;
        var artefactStore = context.ArtefactStore;
        var projectName = job.Source?.GetProject() ?? string.Empty;

        using (_logger.BeginDataScope(DataClassification.Customer))
            _logger.LogInformation("[Teams] Exporting teams for project '{Project}'.", projectName);

        var sink = context.ProgressSink;
        sink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Teams.Export.Started",
            Message = $"Starting team export for project '{projectName}'."
        });

        // Idempotency: check cursor (we still re-enumerate to ensure all team files exist).
        var checkpointing = _checkpointingFactory?.Create(context.StateStore);

        Regex? filterRegex = null;
        if (string.Equals(_options.Scope, "teams", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(_options.Filter))
        {
            filterRegex = new Regex(_options.Filter, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        var count = 0;
        var skipped = 0;
        await foreach (var team in _teamSource.EnumerateTeamsAsync(job.Source ?? throw new InvalidOperationException("Job.Source required for export"), projectName, ct).ConfigureAwait(false))
        {
            // Apply scope/filter
            if (filterRegex is not null && !filterRegex.IsMatch(team.Name))
            {
                _logger.LogDebug("[Teams] Skipping team '{Name}' — does not match filter '{Filter}'.",
                    team.Name, _options.Filter);
                continue;
            }

            var slug = _slugGenerator.GenerateSlug(team.Name);
            var artifactPath = $"Teams/{slug}/team.json";

            // Resume support: skip teams already exported unless AlwaysExport is set.
            if (!_options.AlwaysExport
                && await artefactStore.ExistsAsync(artifactPath, ct).ConfigureAwait(false))
            {
                _logger.LogWarning("[Teams] Skipping already-exported team '{Name}' ({Path}) — use AlwaysExport: true to force re-export.",
                    team.Name, artifactPath);
                skipped++;
                continue;
            }

            var exportTags = new TagList
            {
                { "module", "Teams" },
                { "operation", "teams.export" }
            };
            _migrationMetrics?.IncrementTeamExportInFlight(exportTags);
            var exportSw = Stopwatch.StartNew();
            try
            {
                await _exportOrchestrator.ExportTeamAsync(
                    job.Source!, projectName, team, slug, artefactStore, _options.Extensions, ct).ConfigureAwait(false);

                count++;
                _migrationMetrics?.RecordTeamExportCount(exportTags);
                sink?.Emit(new ProgressEvent
                {
                    Module = ModuleName,
                    Stage = "Teams.Export.Team",
                    Message = $"Exported team '{team.Name}' ({count} total).",
                });
            }
            catch
            {
                _migrationMetrics?.RecordTeamExportError(exportTags);
                throw;
            }
            finally
            {
                exportSw.Stop();
                _migrationMetrics?.DecrementTeamExportInFlight(exportTags);
                _migrationMetrics?.RecordTeamExportDuration(exportSw.Elapsed.TotalMilliseconds, exportTags);
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
                    Teams = new TeamsCounters { Exported = count }
                }
            }
        });

        // Write cursor after successful export.
        if (checkpointing is not null)
        {
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = $"Teams/{count}",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping import.");
            return;
        }

        if (_teamTarget is null)
        {
            _logger.LogWarning("[Teams] No ITeamTarget registered — team import skipped.");
            return;
        }

        if (_importOrchestrator is null)
        {
            _logger.LogWarning("[Teams] No TeamImportOrchestrator registered — team import skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("teams.import");

        var artefactStore = context.ArtefactStore;
        var projectName = context.Job.Target?.GetProject() ?? string.Empty;
        var sourceProjectName = context.Job.Source?.GetProject() ?? projectName;

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

            var json = await artefactStore.ReadAsync(teamPath, ct).ConfigureAwait(false);
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

            var importTags = new TagList
            {
                { "module", "Teams" },
                { "operation", "teams.import" }
            };
            _migrationMetrics?.IncrementTeamImportInFlight(importTags);
            var importSw = Stopwatch.StartNew();
            try
            {
                await _importOrchestrator.ImportTeamAsync(
                    context.Job.Target ?? throw new InvalidOperationException("Job.Target required for import"),
                    projectName, sourceProjectName, teamPackage, _options.Extensions, ct).ConfigureAwait(false);

                count++;
                _migrationMetrics?.RecordTeamImportCount(importTags);
                importSink?.Emit(new ProgressEvent
                {
                    Module = ModuleName,
                    Stage = "Teams.Import.Team",
                    Message = $"Imported team '{teamPackage.Definition?.Name}' ({count} total).",
                });
            }
            catch
            {
                _migrationMetrics?.RecordTeamImportError(importTags);
                throw;
            }
            finally
            {
                importSw.Stop();
                _migrationMetrics?.DecrementTeamImportInFlight(importTags);
                _migrationMetrics?.RecordTeamImportDuration(importSw.Elapsed.TotalMilliseconds, importTags);
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

        // Write cursor after successful import.
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = $"Teams/{count}",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;
        var teamCount = 0;
        var validateTags = new TagList
        {
            { "module", "Teams" },
            { "operation", "teams.validate" }
        };

        await foreach (var teamPath in artefactStore.EnumerateAsync("Teams/", ct).ConfigureAwait(false))
        {
            if (!teamPath.EndsWith("/team.json", StringComparison.OrdinalIgnoreCase))
                continue;

            teamCount++;

            var json = await artefactStore.ReadAsync(teamPath, ct).ConfigureAwait(false);
            if (json is null)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = teamPath,
                    Message = $"[Teams] Team file '{teamPath}' exists but could not be read."
                });
                _migrationMetrics?.RecordTeamValidateError(validateTags);
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
                    _migrationMetrics?.RecordTeamValidateError(validateTags);
                }
                else
                {
                    _migrationMetrics?.RecordTeamValidateCount(validateTags);
                }
            }
            catch (JsonException ex)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = teamPath,
                    Message = $"[Teams] Team file '{teamPath}' contains malformed JSON: {ex.Message}"
                });
                _migrationMetrics?.RecordTeamValidateError(validateTags);
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
}
#endif
