// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class CheckpointingService : ICheckpointingService
{
    private readonly IStateStore _stateStore;
    private readonly ICurrentJobEndpointAccessor? _currentJobEndpointAccessor;
    private readonly ICurrentPackageConfigAccessor? _currentPackageConfigAccessor;

    public CheckpointingService(
        IStateStore stateStore,
        ICurrentJobEndpointAccessor? currentJobEndpointAccessor = null,
        ICurrentPackageConfigAccessor? currentPackageConfigAccessor = null)
    {
        _stateStore = stateStore;
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _currentPackageConfigAccessor = currentPackageConfigAccessor;
    }

    // ── Cursor ──────────────────────────────────────────────────────────

    public async Task<CursorEntry?> ReadCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = ResolveCursorKey(moduleName);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (json is null && TrySplitActionQualifiedModule(moduleName, out _, out var legacyModule))
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
        var key = ResolveCursorKey(moduleName);
        var json = JsonSerializer.Serialize(cursor);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = ResolveCursorKey(moduleName);
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);

        if (TrySplitActionQualifiedModule(moduleName, out _, out var legacyModule))
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

        if (json is null && TrySplitActionQualifiedModule(moduleName, out _, out var legacyModule))
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

        if (TrySplitActionQualifiedModule(moduleName, out _, out var legacyModule))
        {
            await _stateStore.DeleteAsync(PackagePaths.ContinuationFile(moduleName), cancellationToken).ConfigureAwait(false);
            await _stateStore.DeleteAsync(PackagePaths.ContinuationFile(legacyModule), cancellationToken).ConfigureAwait(false);
        }
    }

    private string ResolveCursorKey(string moduleName)
    {
        if (TrySplitActionQualifiedModule(moduleName, out var action, out var module) &&
            TryResolveEndpoint(out var endpointUrl, out var projectName))
        {
            return PackagePaths.CursorFile(action, module, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out action) &&
            TryResolveEndpoint(out endpointUrl, out projectName))
        {
            return PackagePaths.CursorFile(action, moduleName, endpointUrl, projectName);
        }

        return PackagePaths.CursorFile(moduleName);
    }

    private string ResolveContinuationKey(string moduleName)
    {
        if (TrySplitActionQualifiedModule(moduleName, out var action, out var module) &&
            TryResolveEndpoint(out var endpointUrl, out var projectName))
        {
            return PackagePaths.ContinuationFile(action, module, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out action) &&
            TryResolveEndpoint(out endpointUrl, out projectName))
        {
            return PackagePaths.ContinuationFile(action, moduleName, endpointUrl, projectName);
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

    private static bool TrySplitActionQualifiedModule(string moduleName, out string action, out string module)
    {
        var separatorIndex = moduleName.IndexOf('.');
        if (separatorIndex > 0 && separatorIndex < moduleName.Length - 1)
        {
            action = moduleName.Substring(0, separatorIndex);
            module = moduleName.Substring(separatorIndex + 1);
            return true;
        }

        action = string.Empty;
        module = string.Empty;
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
