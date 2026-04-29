using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// <see cref="IModule"/> implementation for identity export/import.
/// Exports identity descriptors from the source system to <c>Identities/descriptors.jsonl</c>
/// and makes the mapping available to downstream modules via <see cref="IIdentityMappingService"/>.
/// </summary>
public sealed class IdentitiesModule : IModule
{
    private const string DescriptorsPath = "Identities/descriptors.jsonl";
    private const string MappingPath = "Identities/mapping.json";
    private const string UnresolvedPath = "Identities/unresolved.json";
    private const string ModuleName = "Identities";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IIdentitySource? _identitySource;
#if !NET481
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly IMigrationMetrics? _migrationMetrics;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<IdentitiesModule> _logger;
    private readonly IdentitiesModuleOptions _options;

    public string Name => ModuleName;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public IdentitiesModule(
        ILogger<IdentitiesModule> logger,
        IOptions<IdentitiesModuleOptions> options,
        IIdentitySource? identitySource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null
#if !NET481
        , IIdentityLookupTool? identityLookupTool = null
        , IMigrationMetrics? migrationMetrics = null
#endif
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _identitySource = identitySource;
        _checkpointingFactory = checkpointingFactory;
#if !NET481
        _identityLookupTool = identityLookupTool;
        _migrationMetrics = migrationMetrics;
#endif
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping export.");
            return;
        }

        if (_identitySource is null)
        {
            _logger.LogWarning("[Identities] No IIdentitySource registered — identity export skipped.");
            return;
        }

        var job = context.Job;
        var artefactStore = context.ArtefactStore;
        var stateStore = context.StateStore;

        var project = job.Source?.GetProject() ?? string.Empty;

        // Idempotency: skip if already completed.
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(stateStore);
            var cursor = await checkpointing.ReadCursorAsync(ModuleName, ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await artefactStore.ExistsAsync(DescriptorsPath, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("[Identities] Already exported (cursor found) — skipping re-export.");
                return;
            }
        }

        using var activity = s_activitySource.StartActivity("identities.export");
        activity?.SetTag("project", project);

#if !NET481
        using (_logger.BeginDataScope(DataClassification.Customer))
#endif
        _logger.LogInformation("[Identities] Starting identity export for project '{Project}'.", project);

        var sink = context.ProgressSink;
        sink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Export.Started",
            Message = $"Starting identity export for project '{project}'.",
        });

        var count = 0;
#if !NET481
        var exportTags = new TagList
        {
            { "module", "Identities" },
            { "operation", "identities.export" }
        };
        _migrationMetrics?.IncrementIdentityExportInFlight(exportTags);
#endif
        var exportSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await foreach (var descriptor in _identitySource.EnumerateIdentitiesAsync(job.Source ?? throw new InvalidOperationException("Job.Source required for identity export"), project, ct).ConfigureAwait(false))
            {
                var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);
                await artefactStore.AppendAsync(DescriptorsPath, line + "\n", ct).ConfigureAwait(false);
                count++;
#if !NET481
                _migrationMetrics?.RecordIdentityExportCount(exportTags);
#endif
            }
        }
        catch
        {
#if !NET481
            _migrationMetrics?.RecordIdentityExportError(exportTags);
#endif
            throw;
        }
        finally
        {
            exportSw.Stop();
#if !NET481
            _migrationMetrics?.DecrementIdentityExportInFlight(exportTags);
            _migrationMetrics?.RecordIdentityExportDuration(exportSw.Elapsed.TotalMilliseconds, exportTags);
#endif
        }

        activity?.SetTag("identities.count", count);
        _logger.LogInformation("[Identities] Exported {Count} identity descriptors to {Path}.", count, DescriptorsPath);
        sink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Export.Complete",
            Message = $"Identity export complete — {count} descriptors exported.",
            Metrics = new JobMetrics
            {
                Migration = new MigrationCounters
                {
                    Identities = new IdentitiesCounters { Exported = count }
                }
            }
        });

        // Write cursor after successful export.
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(stateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = DescriptorsPath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[Identities] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping import.");
            return;
        }

        var artefactStore = context.ArtefactStore;

        using var activity = s_activitySource.StartActivity("identities.import");

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Import.Started",
            Message = "Starting identity import.",
        });

        var descriptorsJson = await artefactStore.ReadAsync(DescriptorsPath, ct).ConfigureAwait(false);
        if (descriptorsJson is null)
        {
            _logger.LogWarning("[Identities] {Path} not found in package — identity mapping will not be available.", DescriptorsPath);
            return;
        }

        var importTags = new TagList
        {
            { "module", "Identities" },
            { "operation", "identities.import" }
        };
        var importSw = System.Diagnostics.Stopwatch.StartNew();

        if (_identityLookupTool is not null)
        {
            await _identityLookupTool.InitializeAsync(artefactStore, ct).ConfigureAwait(false);
        }

        var resolvedCount = CountLines(descriptorsJson);
        for (int i = 0; i < resolvedCount; i++)
            _migrationMetrics?.RecordIdentityImportResolved(importTags);

        if (_identityLookupTool is not null)
        {
            await _identityLookupTool.WriteUnresolvedAsync(artefactStore, ct).ConfigureAwait(false);
        }

        importSw.Stop();
        _migrationMetrics?.RecordIdentityImportDuration(importSw.Elapsed.TotalMilliseconds, importTags);

        var hasMapping = await artefactStore.ExistsAsync(MappingPath, ct).ConfigureAwait(false);
        _logger.LogInformation("[Identities] Identity import complete: {Resolved} resolved, mapping overrides: {HasMapping}.", resolvedCount, hasMapping);
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Import.Complete",
            Message = $"Identity import complete — {resolvedCount} resolved.",
        });

        activity?.SetTag("identities.descriptor.resolved", resolvedCount);
        activity?.SetTag("identities.has.mapping", hasMapping);
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;
#if !NET481
        var validateTags = new TagList
        {
            { "module", "Identities" },
            { "operation", "identities.validate" }
        };
#endif

        var exists = await artefactStore.ExistsAsync(DescriptorsPath, ct).ConfigureAwait(false);
        if (!exists)
        {
            context.Errors.Add(new ValidationError
            {
                Path = DescriptorsPath,
                Message = $"[Identities] Required file '{DescriptorsPath}' is missing from the package."
            });
#if !NET481
            _migrationMetrics?.RecordIdentityValidateError(validateTags);
#endif
            return;
        }

        var content = await artefactStore.ReadAsync(DescriptorsPath, ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = DescriptorsPath,
                Message = $"[Identities] File '{DescriptorsPath}' exists but could not be read."
            });
#if !NET481
            _migrationMetrics?.RecordIdentityValidateError(validateTags);
#endif
            return;
        }

        var lineNumber = 0;
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("descriptor", out _) &&
                    !root.TryGetProperty("Descriptor", out _))
                {
                    context.Errors.Add(new ValidationError
                    {
                        Path = DescriptorsPath,
                        Message = $"[Identities] Line {lineNumber} in '{DescriptorsPath}' is missing required field 'descriptor'."
                    });
#if !NET481
                    _migrationMetrics?.RecordIdentityValidateError(validateTags);
#endif
                }
                else
                {
#if !NET481
                    _migrationMetrics?.RecordIdentityValidateCount(validateTags);
#endif
                }
            }
            catch (JsonException ex)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = DescriptorsPath,
                    Message = $"[Identities] Line {lineNumber} in '{DescriptorsPath}' is malformed JSON: {ex.Message}"
                });
#if !NET481
                _migrationMetrics?.RecordIdentityValidateError(validateTags);
#endif
            }
        }
    }

    private static int CountLines(string content)
    {
        var count = 0;
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
                count++;
        }
        return count;
    }
}
