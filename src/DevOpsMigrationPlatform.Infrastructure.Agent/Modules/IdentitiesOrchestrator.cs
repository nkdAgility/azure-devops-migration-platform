// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
#endif
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates identity export, import, and validation operations.
/// Handles JSONL streaming, checkpointing, progress events, and metrics — delegates
/// the actual identity enumeration to <see cref="IIdentitySource"/> and mapping to
/// <see cref="IIdentityLookupTool"/>.
/// </summary>
internal sealed class IdentitiesOrchestrator : IIdentitiesOrchestrator
{
    private const string DescriptorsPath = "Identities/descriptors.jsonl";
    private const string MappingPath = "Identities/mapping.json";
    private const string ModuleName = "Identities";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly IPackage? _package;

    public IdentitiesOrchestrator(
        ILogger<IdentitiesOrchestrator> logger,
        IPlatformMetrics? PlatformMetrics = null,
        IPackage? package = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _PlatformMetrics = PlatformMetrics;
        _package = package;
    }

    /// <summary>
    /// Exports identity descriptors from <paramref name="identitySource"/> to JSONL.
    /// Idempotent — skips if cursor shows completed and artefact exists.
    /// </summary>
    public async Task ExportAsync(
        IIdentitySource identitySource,
        ExportContext context,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;
        var stateStore = context.StateStore;

        // Idempotency: skip if already completed.
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(stateStore);
            var cursor = await checkpointing.ReadCursorAsync("export.identities", ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await PackageAccess.ExistsAsync(_package, artefactStore, DescriptorsPath, ct).ConfigureAwait(false))
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
        var exportTags = new MetricsTagList
        {
            new("module", "Identities"),
            new("operation", "identities.export")
        };
        _PlatformMetrics?.IncrementIdentityExportInFlight(exportTags);
        var exportSw = Stopwatch.StartNew();
        try
        {
            await foreach (var descriptor in identitySource.EnumerateIdentitiesAsync(project, ct).ConfigureAwait(false))
            {
                var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);
                await PackageAccess.AppendTextAsync(_package, artefactStore, DescriptorsPath, line + "\n", ct).ConfigureAwait(false);
                count++;
                _PlatformMetrics?.RecordIdentityExportCount(exportTags);
            }
        }
        catch
        {
            _PlatformMetrics?.RecordIdentityExportError(exportTags);
            throw;
        }
        finally
        {
            exportSw.Stop();
            _PlatformMetrics?.DecrementIdentityExportInFlight(exportTags);
            _PlatformMetrics?.RecordIdentityExportDuration(exportSw.Elapsed.TotalMilliseconds, exportTags);
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
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(stateStore);
            await checkpointing.WriteCursorAsync("export.identities", new CursorEntry
            {
                LastProcessed = DescriptorsPath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

#if !NET481
    /// <summary>
    /// Imports identity descriptors: initialises the lookup tool, counts resolved entries,
    /// and writes unresolved identities to the package.
    /// </summary>
    public async Task ImportAsync(
        IIdentityLookupTool? identityLookupTool,
        ImportContext context,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;

        using var activity = s_activitySource.StartActivity("identities.import");

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Import.Started",
            Message = "Starting identity import.",
        });

        var descriptorsJson = await ReadPackageContentAsync(artefactStore, DescriptorsPath, ct).ConfigureAwait(false);
        if (descriptorsJson is null)
        {
            _logger.LogWarning("[Identities] {Path} not found in package — identity mapping will not be available.", DescriptorsPath);
            return;
        }

        var importTags = new MetricsTagList
        {
            { "module", "Identities" },
            { "operation", "identities.import" }
        };
        var importSw = Stopwatch.StartNew();

        if (identityLookupTool is not null)
        {
            await identityLookupTool.InitializeAsync(artefactStore, ct).ConfigureAwait(false);
        }

        var resolvedCount = CountLines(descriptorsJson);
        for (int i = 0; i < resolvedCount; i++)
            _PlatformMetrics?.RecordIdentityImportResolved(importTags);

        if (identityLookupTool is not null)
        {
            await identityLookupTool.WriteUnresolvedAsync(artefactStore, ct).ConfigureAwait(false);
        }

        importSw.Stop();
        _PlatformMetrics?.RecordIdentityImportDuration(importSw.Elapsed.TotalMilliseconds, importTags);

        var hasMapping = await ExistsInPackageAsync(artefactStore, MappingPath, ct).ConfigureAwait(false);
        _logger.LogInformation("[Identities] Identity import complete: {Resolved} resolved, mapping overrides: {HasMapping}.", resolvedCount, hasMapping);
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Import.Complete",
            Message = $"Identity import complete — {resolvedCount} resolved.",
        });

        activity?.SetTag("identities.descriptor.resolved", resolvedCount);
        activity?.SetTag("identities.has.mapping", hasMapping);
    }
#endif

    /// <summary>
    /// Validates the identity descriptors JSONL artefact exists, is readable, and each
    /// line contains a <c>descriptor</c> field.
    /// </summary>
    public async Task ValidateAsync(
        IArtefactStore artefactStore,
        ValidationContext context,
        CancellationToken ct)
    {
        var validateTags = new MetricsTagList
        {
            new("module", "Identities"),
            new("operation", "identities.validate")
        };

        var content = await ReadPackageContentAsync(artefactStore, DescriptorsPath, ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = DescriptorsPath,
                Message = $"[Identities] Required file '{DescriptorsPath}' is missing from the package."
            });
            _PlatformMetrics?.RecordIdentityValidateError(validateTags);
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
                    _PlatformMetrics?.RecordIdentityValidateError(validateTags);
                }
                else
                {
                    _PlatformMetrics?.RecordIdentityValidateCount(validateTags);
                }
            }
            catch (JsonException ex)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = DescriptorsPath,
                    Message = $"[Identities] Line {lineNumber} in '{DescriptorsPath}' is malformed JSON: {ex.Message}"
                });
                _PlatformMetrics?.RecordIdentityValidateError(validateTags);
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

    private async Task<string?> ReadPackageContentAsync(IArtefactStore artefactStore, string relativePath, CancellationToken ct)
    {
        return await PackageAccess.ReadTextAsync(_package, artefactStore, relativePath, ct).ConfigureAwait(false);
    }

    private async Task<bool> ExistsInPackageAsync(IArtefactStore artefactStore, string relativePath, CancellationToken ct)
    {
        return await PackageAccess.ExistsAsync(_package, artefactStore, relativePath, ct).ConfigureAwait(false);
    }
}

