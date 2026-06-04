// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
#endif
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates identity export, import, and validation operations.
/// Handles JSONL streaming, checkpointing, progress events, and metrics — delegates
/// the actual identity enumeration to <see cref="IIdentitySource"/> and mapping to
/// <see cref="IIdentityTranslationTool"/>.
/// </summary>
internal sealed class IdentitiesOrchestrator : IIdentitiesOrchestrator
{
    private const string ModuleName = "Identities";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly IPackageAccess? _package;

    public IdentitiesOrchestrator(
        ILogger<IdentitiesOrchestrator> logger,
        IPlatformMetrics? PlatformMetrics = null,
        IPackageAccess? package = null)
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
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct)
    {
        var package = context.Package;

        // Idempotency: skip if already completed.
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(package);
            var cursor = await checkpointing.ReadCursorAsync("export.identities", ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await ExistsInPackageAsync(organisation, project, "descriptors.jsonl", ct).ConfigureAwait(false))
            {
                _logger.LogInformation("[Identities] Already exported (cursor found) — skipping re-export.");
                return;
            }
        }

        using var activity = s_activitySource.StartActivity("identities.export");
        activity?.SetTag("organisation", organisation);
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
                await AppendPackageTextAsync(organisation, project, "descriptors.jsonl", line + "\n", ct).ConfigureAwait(false);
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
        _logger.LogInformation("[Identities] Exported {Count} identity descriptors.", count);
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
            var checkpointing = checkpointingFactory.Create(package);
            await checkpointing.WriteCursorAsync("export.identities", new CursorEntry
            {
                LastProcessed = "descriptors.jsonl",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = count
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports identity descriptors: initialises the translation tool, counts resolved entries,
    /// and writes unresolved identities to the package. Unconditional per FR-020 (no interface
    /// guard); on net481 this is unreachable because <c>IdentitiesModule</c> skips import.
    /// </summary>
    public async Task ImportAsync(
        IIdentityTranslationTool? identityTranslationTool,
        ImportContext context,
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("identities.import");

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Identities.Import.Started",
            Message = "Starting identity import.",
        });

        var descriptorsJson = await ReadPackageContentAsync(organisation, project, "descriptors.jsonl", ct).ConfigureAwait(false);
        if (descriptorsJson is null)
        {
            _logger.LogWarning("[Identities] descriptors.jsonl not found in package — identity mapping will not be available.");
            return;
        }

        var importTags = new MetricsTagList
        {
            { "module", "Identities" },
            { "operation", "identities.import" }
        };
        var importSw = Stopwatch.StartNew();

        if (identityTranslationTool is not null)
        {
            await identityTranslationTool.InitializeAsync(ct).ConfigureAwait(false);
        }

        var resolvedCount = CountLines(descriptorsJson);
        for (int i = 0; i < resolvedCount; i++)
            _PlatformMetrics?.RecordIdentityImportResolved(importTags);

        if (identityTranslationTool is not null)
        {
            await identityTranslationTool.WriteUnresolvedAsync(ct).ConfigureAwait(false);
        }

        importSw.Stop();
        _PlatformMetrics?.RecordIdentityImportDuration(importSw.Elapsed.TotalMilliseconds, importTags);

        var hasMapping = await ExistsInPackageAsync(organisation, project, "mapping.json", ct).ConfigureAwait(false);
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

    /// <summary>
    /// Validates the identity descriptors JSONL artefact exists, is readable, and each
    /// line contains a <c>descriptor</c> field.
    /// </summary>
    public async Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct)
    {
        var validateTags = new MetricsTagList
        {
            new("module", "Identities"),
            new("operation", "identities.validate")
        };

        var content = await ReadPackageContentAsync(package, organisation, project, "descriptors.jsonl", ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = "descriptors.jsonl",
                Message = "[Identities] Required file 'descriptors.jsonl' is missing from the package."
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
                        Path = "descriptors.jsonl",
                        Message = $"[Identities] Line {lineNumber} in 'descriptors.jsonl' is missing required field 'descriptor'."
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
                    Path = "descriptors.jsonl",
                    Message = $"[Identities] Line {lineNumber} in 'descriptors.jsonl' is malformed JSON: {ex.Message}"
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

    private async Task<string?> ReadPackageContentAsync(string organisation, string project, string fileName, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        return await ReadPackageContentAsync(_package, organisation, project, fileName, ct).ConfigureAwait(false);
    }

    private static async Task<string?> ReadPackageContentAsync(IPackageAccess package, string organisation, string project, string fileName, CancellationToken ct)
    {
        var payload = await package.RequestContentAsync(
            CreatePackageContentContext(organisation, project, fileName),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private async Task<bool> ExistsInPackageAsync(string organisation, string project, string fileName, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        return await _package.ContentExistsAsync(
            CreatePackageContentContext(organisation, project, fileName),
            ct).ConfigureAwait(false);
    }

    private async Task AppendPackageTextAsync(string organisation, string project, string fileName, string content, CancellationToken ct)
    {
        if (_package is null)
            throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for package content operations.");

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false);
        await _package.AppendContentAsync(
            CreatePackageContentContext(organisation, project, fileName),
            new PackagePayload(stream, "application/x-ndjson"),
            ct).ConfigureAwait(false);
    }

    private static PackageContentContext CreatePackageContentContext(string organisation, string project, string fileName)
        => new(
            PackageContentKind.Artefact,
            Organisation: organisation,
            Project: project,
            Module: ModuleName,
            Address: new RelativePathAddress(fileName));
}

