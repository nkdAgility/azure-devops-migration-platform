#if !NET481
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
using DevOpsMigrationPlatform.Abstractions.Validation;
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
    private const string ModuleName = "Identities";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IIdentitySource? _identitySource;
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<IdentitiesModule> _logger;
    private readonly IdentitiesModuleOptions _options;

    public string Name => ModuleName;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public IdentitiesModule(
        ILogger<IdentitiesModule> logger,
        IOptions<IdentitiesModuleOptions> options,
        IIdentitySource? identitySource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _identitySource = identitySource;
        _checkpointingFactory = checkpointingFactory;
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

        _logger.LogInformation("[Identities] Starting identity export for project '{Project}'.", project);

        var count = 0;

        await foreach (var descriptor in _identitySource.EnumerateIdentitiesAsync(job.Source ?? throw new InvalidOperationException("Job.Source required for identity export"), project, ct).ConfigureAwait(false))
        {
            var line = JsonSerializer.Serialize(descriptor, s_jsonOptions);
            await artefactStore.AppendAsync(DescriptorsPath, line + "\n", ct).ConfigureAwait(false);
            count++;
        }

        activity?.SetTag("identities.count", count);
        _logger.LogInformation("[Identities] Exported {Count} identity descriptors to {Path}.", count, DescriptorsPath);

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
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping import.");
            return;
        }

        var artefactStore = context.ArtefactStore;

        using var activity = s_activitySource.StartActivity("identities.import");

        var descriptorsJson = await artefactStore.ReadAsync(DescriptorsPath, ct).ConfigureAwait(false);
        if (descriptorsJson is null)
        {
            _logger.LogWarning("[Identities] {Path} not found in package — identity mapping will not be available.", DescriptorsPath);
            return;
        }

        var mappingJson = await artefactStore.ReadAsync(MappingPath, ct).ConfigureAwait(false);
        var descriptorCount = CountLines(descriptorsJson);
        var hasMapping = mappingJson is not null;

        _logger.LogInformation(
            "[Identities] Identity package loaded: {DescriptorCount} descriptors, mapping overrides: {HasMapping}.",
            descriptorCount, hasMapping);

        activity?.SetTag("identities.descriptor.count", descriptorCount);
        activity?.SetTag("identities.has.mapping", hasMapping);
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;

        var exists = await artefactStore.ExistsAsync(DescriptorsPath, ct).ConfigureAwait(false);
        if (!exists)
        {
            context.Errors.Add(new ValidationError
            {
                Path = DescriptorsPath,
                Message = $"[Identities] Required file '{DescriptorsPath}' is missing from the package."
            });
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
            return;
        }

        var lineNumber = 0;
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
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
                }
            }
            catch (JsonException ex)
            {
                context.Errors.Add(new ValidationError
                {
                    Path = DescriptorsPath,
                    Message = $"[Identities] Line {lineNumber} in '{DescriptorsPath}' is malformed JSON: {ex.Message}"
                });
            }
        }
    }

    private static int CountLines(string content)
    {
        var count = 0;
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
                count++;
        }
        return count;
    }
}
#endif
