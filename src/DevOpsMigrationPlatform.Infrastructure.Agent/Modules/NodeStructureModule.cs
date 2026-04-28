#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
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
/// <see cref="IModule"/> implementation for classification tree (node structure) export/import.
/// Wraps the existing <see cref="IClassificationTreeCapture"/> and <see cref="INodeEnsurer"/>
/// tools with the module lifecycle contract.
/// 
/// Note on localised root names: <see cref="IClassificationTreeCapture"/> normalises
/// German "Bereich"/"Iteration" roots to English "Area"/"Iteration" in the captured JSON.
/// </summary>
public sealed class NodeStructureModule : IModule
{
    private const string SourceTreePath = "Nodes/source-tree.json";
    private const string ModuleName = "Nodes";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IClassificationTreeCapture? _capture;
    private readonly INodeEnsurer? _nodeEnsurer;
    private readonly ILogger<NodeStructureModule> _logger;
    private readonly NodeStructureModuleOptions _options;

    public string Name => ModuleName;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public NodeStructureModule(
        ILogger<NodeStructureModule> logger,
        IOptions<NodeStructureModuleOptions> options,
        IClassificationTreeCapture? capture = null,
        INodeEnsurer? nodeEnsurer = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _capture = capture;
        _nodeEnsurer = nodeEnsurer;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping export.");
            return;
        }

        if (_capture is null)
        {
            _logger.LogWarning("[Nodes] No IClassificationTreeCapture registered — node export skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("nodes.export");

        var endpoint = context.Job.Source
            ?? throw new InvalidOperationException("Job.Source is required for node export.");

        await _capture.CaptureAsync(context.ArtefactStore, endpoint, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping import.");
            return;
        }

        if (_nodeEnsurer is null)
        {
            _logger.LogWarning("[Nodes] No INodeEnsurer registered — node import skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("nodes.import");

        var endpoint = context.Job.Target
            ?? throw new InvalidOperationException("Job.Target is required for node import.");

        var project = endpoint.GetProject();
        var mapping = new ProjectMapping(project, project);

        if (_options.ReplicateSourceTree)
        {
            _logger.LogInformation("[Nodes] Replicating source tree.");
            await _nodeEnsurer.ReplicateSourceTreeAsync(
                mapping, endpoint,
                context.ArtefactStore, context.StateStore,
                ct).ConfigureAwait(false);
        }

        if (_options.AutoCreateNodes)
        {
            _logger.LogInformation("[Nodes] Ensuring referenced paths exist.");
            await _nodeEnsurer.EnsureReferencedPathsAsync(
                mapping, endpoint,
                context.ArtefactStore,
                ct).ConfigureAwait(false);
        }

        if (!_options.ReplicateSourceTree && !_options.AutoCreateNodes)
        {
            _logger.LogDebug("[Nodes] Both ReplicateSourceTree and AutoCreateNodes are disabled — nothing to import.");
        }
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;

        var exists = await artefactStore.ExistsAsync(SourceTreePath, ct).ConfigureAwait(false);
        if (!exists)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] Required file '{SourceTreePath}' is missing from the package."
            });
            return;
        }

        var content = await artefactStore.ReadAsync(SourceTreePath, ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] File '{SourceTreePath}' exists but could not be read."
            });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] File '{SourceTreePath}' contains malformed JSON: {ex.Message}"
            });
        }
    }
}
#endif
