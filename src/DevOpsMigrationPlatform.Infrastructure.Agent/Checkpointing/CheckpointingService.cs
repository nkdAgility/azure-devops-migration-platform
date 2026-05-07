// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class CheckpointingService : ICheckpointingService
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);
    private readonly IStateStore _stateStore;
    private readonly ICurrentJobEndpointAccessor? _currentJobEndpointAccessor;
    private readonly ICurrentPackageConfigAccessor? _currentPackageConfigAccessor;
    private readonly ILogger<CheckpointingService>? _logger;

    public CheckpointingService(
        IStateStore stateStore,
        ICurrentJobEndpointAccessor? currentJobEndpointAccessor = null,
        ICurrentPackageConfigAccessor? currentPackageConfigAccessor = null,
        ILogger<CheckpointingService>? logger = null)
    {
        _stateStore = stateStore;
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _currentPackageConfigAccessor = currentPackageConfigAccessor;
        _logger = logger;
    }

    // ── Cursor ──────────────────────────────────────────────────────────

    public async Task<CursorEntry?> ReadCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "read");
        activity?.SetTag("module.name", moduleName);
        var key = ResolveCursorKey(moduleName);
        activity?.SetTag("cursor.key", key);
        _logger?.LogDebug("Reading cursor from {CursorKey}.", key);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (json is null && StateCursorIdentity.TryParse(moduleName, out _, out var legacyModule))
        {
            json = await _stateStore.ReadAsync(PackagePaths.CursorFile(moduleName), cancellationToken).ConfigureAwait(false);
            json ??= await _stateStore.ReadAsync(PackagePaths.CursorFile(legacyModule), cancellationToken).ConfigureAwait(false);
        }

        if (json is null)
            return null;
        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(string moduleName, CursorEntry cursor, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("module.name", moduleName);
        var key = ResolveCursorKey(moduleName);
        activity?.SetTag("cursor.key", key);
        _logger?.LogDebug("Writing cursor to {CursorKey}.", key);
        var json = JsonSerializer.Serialize(cursor);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = ResolveCursorKey(moduleName);
        _logger?.LogDebug("Deleting cursor at {CursorKey}.", key);
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

        if (StateCursorIdentity.TryParse(moduleName, out _, out var legacyModule))
        {
            await _stateStore.DeleteAsync(PackagePaths.CursorFile(moduleName), cancellationToken).ConfigureAwait(false);
            await _stateStore.DeleteAsync(PackagePaths.CursorFile(legacyModule), cancellationToken).ConfigureAwait(false);
        }
    }

    // ── Continuation Token (Resumable Batching) ─────────────────────────

    public async Task<BatchContinuationToken?> ReadContinuationTokenAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = ResolveContinuationKey(moduleName);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (json is null && StateCursorIdentity.TryParse(moduleName, out _, out var legacyModule))
        {
            json = await _stateStore.ReadAsync(PackagePaths.ContinuationFile(moduleName), cancellationToken).ConfigureAwait(false);
            json ??= await _stateStore.ReadAsync(PackagePaths.ContinuationFile(legacyModule), cancellationToken).ConfigureAwait(false);
        }
        if (json is null)
            return null;
        return JsonSerializer.Deserialize<BatchContinuationToken>(json);
    }

    public async Task WriteContinuationTokenAsync(string moduleName, BatchContinuationToken token, CancellationToken cancellationToken)
    {
        var key = ResolveContinuationKey(moduleName);
        var json = JsonSerializer.Serialize(token);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteContinuationTokenAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = ResolveContinuationKey(moduleName);
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

        if (StateCursorIdentity.TryParse(moduleName, out _, out var legacyModule))
        {
            await _stateStore.DeleteAsync(PackagePaths.ContinuationFile(moduleName), cancellationToken).ConfigureAwait(false);
            await _stateStore.DeleteAsync(PackagePaths.ContinuationFile(legacyModule), cancellationToken).ConfigureAwait(false);
        }
    }

    private string ResolveCursorKey(string moduleName)
    {
        if (StateCursorIdentity.TryParse(moduleName, out var parsedAction, out var parsedModule) &&
            TryResolveEndpoint(out var endpointUrl, out var projectName))
        {
            return PackagePaths.CursorFile(parsedAction, parsedModule, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveEndpoint(out var configEndpointUrl, out var configProjectName))
        {
            return PackagePaths.CursorFile(configAction, moduleName, configEndpointUrl, configProjectName);
        }

        return PackagePaths.CursorFile(moduleName);
    }

    private string ResolveContinuationKey(string moduleName)
    {
        if (StateCursorIdentity.TryParse(moduleName, out var parsedAction, out var parsedModule) &&
            TryResolveEndpoint(out var endpointUrl, out var projectName))
        {
            return PackagePaths.ContinuationFile(parsedAction, parsedModule, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveEndpoint(out var configEndpointUrl, out var configProjectName))
        {
            return PackagePaths.ContinuationFile(configAction, moduleName, configEndpointUrl, configProjectName);
        }

        return PackagePaths.ContinuationFile(moduleName);
    }

    private bool TryResolveEndpoint(out string endpointUrl, out string projectName)
    {
        var source = _currentJobEndpointAccessor?.Source;
        if (source is not null &&
            !string.IsNullOrWhiteSpace(source.Url) &&
            !string.IsNullOrWhiteSpace(source.Project))
        {
            endpointUrl = source.Url;
            projectName = source.Project;
            return true;
        }

        var target = _currentJobEndpointAccessor?.Target;
        if (target is not null &&
            !string.IsNullOrWhiteSpace(target.Url) &&
            !string.IsNullOrWhiteSpace(target.Project))
        {
            endpointUrl = target.Url;
            projectName = target.Project;
            return true;
        }

        endpointUrl = string.Empty;
        projectName = string.Empty;
        return false;
    }

    private bool TryResolveActionFromConfig(out string action)
    {
        var mode =
            _currentPackageConfigAccessor?.Current?["MigrationPlatform:Mode"] ??
            _currentPackageConfigAccessor?.Current?["Mode"];

        if (string.IsNullOrWhiteSpace(mode))
        {
            action = string.Empty;
            return false;
        }

        var normalized = mode!.Trim().ToLowerInvariant();
        if (normalized is "export" or "import" or "inventory" or "prepare" or "validate" or "dependencies")
        {
            action = normalized;
            return true;
        }

        action = string.Empty;
        return false;
    }
}
