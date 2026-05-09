// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
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

    public async Task<CursorEntry?> ReadCursorAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "read");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var key = ResolveCursorKey(checkpointIdentity);
        activity?.SetTag("cursor.key", key);
        _logger?.LogDebug("Reading cursor from {CursorKey}.", key);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (json is null)
            return null;
        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(string checkpointIdentity, CursorEntry cursor, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var key = ResolveCursorKey(checkpointIdentity);
        activity?.SetTag("cursor.key", key);
        _logger?.LogDebug("Writing cursor to {CursorKey}.", key);
        var json = JsonSerializer.Serialize(cursor);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        foreach (var key in ResolveCursorDeleteKeys(checkpointIdentity))
        {
            _logger?.LogDebug("Deleting cursor at {CursorKey}.", key);
            await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    // ── Continuation Token (Resumable Batching) ─────────────────────────

    public async Task<BatchContinuationToken?> ReadContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        var key = ResolveContinuationKey(checkpointIdentity);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;
        return JsonSerializer.Deserialize<BatchContinuationToken>(json);
    }

    public async Task WriteContinuationTokenAsync(string checkpointIdentity, BatchContinuationToken token, CancellationToken cancellationToken)
    {
        var key = ResolveContinuationKey(checkpointIdentity);
        var json = JsonSerializer.Serialize(token);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        foreach (var key in ResolveContinuationDeleteKeys(checkpointIdentity))
        {
            await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<string> ResolveCursorDeleteKeys(string checkpointIdentity)
        => ResolveDeleteKeys(
            checkpointIdentity,
            ResolveCursorKey,
            (action, scopeName, endpointUrl, projectName) => PackagePaths.CursorFile(action, scopeName, endpointUrl, projectName),
            (action, scopeName, orgFolder, projectName) => $"{orgFolder}/{projectName}/.migration/{action.ToLowerInvariant()}.{scopeName.ToLowerInvariant()}.cursor.json",
            "cursor");

    private IEnumerable<string> ResolveContinuationDeleteKeys(string checkpointIdentity)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedScopeName) &&
            TryResolveEndpoint(out var endpointUrl, out var projectName))
        {
            return [PackagePaths.ContinuationFile(parsedAction, parsedScopeName, endpointUrl, projectName)];
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveEndpoint(out var configEndpointUrl, out var configProjectName))
        {
            return [PackagePaths.ContinuationFile(configAction, checkpointIdentity, configEndpointUrl, configProjectName)];
        }

        if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
            TryResolveConfiguredProjectScopes(out var configuredScopes))
        {
            return configuredScopes
                .Select(scope => string.IsNullOrWhiteSpace(scope.EndpointUrl)
                    ? $"{scope.OrgFolder}/{scope.ProjectName}/.migration/{action.ToLowerInvariant()}.{scopeName.ToLowerInvariant()}.continuation.json"
                    : PackagePaths.ContinuationFile(action, scopeName, scope.EndpointUrl, scope.ProjectName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (!checkpointIdentity.Contains("."))
        {
            return [PackagePaths.ContinuationFile(checkpointIdentity)];
        }

        throw new InvalidOperationException(
            $"Checkpoint scope could not be resolved for continuation token '{checkpointIdentity}'. Project-scoped checkpoint operations require both action and endpoint context.");
    }

    private IEnumerable<string> ResolveDeleteKeys(
        string checkpointIdentity,
        Func<string, string> directResolver,
        Func<string, string, string, string, string> endpointScopedFactory,
        Func<string, string, string, string, string> configScopedFactory,
        string stateKind)
    {
        try
        {
            return [directResolver(checkpointIdentity)];
        }
        catch (InvalidOperationException)
        {
            if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
                TryResolveConfiguredProjectScopes(out var configuredScopes))
            {
                return configuredScopes
                    .Select(scope => string.IsNullOrWhiteSpace(scope.EndpointUrl)
                        ? configScopedFactory(action, scopeName, scope.OrgFolder, scope.ProjectName)
                        : endpointScopedFactory(action, scopeName, scope.EndpointUrl, scope.ProjectName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

                    return Array.Empty<string>();
        }
    }

    private string ResolveCursorKey(string checkpointIdentity)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedScopeName) &&
            TryResolveLiveEndpointScope(out var endpointUrl, out var orgFolder, out var projectName))
        {
            return string.IsNullOrWhiteSpace(endpointUrl)
                ? $"{orgFolder}/{projectName}/.migration/{parsedAction.ToLowerInvariant()}.{parsedScopeName.ToLowerInvariant()}.cursor.json"
                : PackagePaths.CursorFile(parsedAction, parsedScopeName, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveLiveEndpointScope(out var configEndpointUrl, out var configOrgFolder, out var configProjectName))
        {
            return string.IsNullOrWhiteSpace(configEndpointUrl)
                ? $"{configOrgFolder}/{configProjectName}/.migration/{configAction.ToLowerInvariant()}.{checkpointIdentity.ToLowerInvariant()}.cursor.json"
                : PackagePaths.CursorFile(configAction, checkpointIdentity, configEndpointUrl, configProjectName);
        }

        if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
            TryResolveSingleConfiguredProjectScope(out var configuredScope))
        {
            return BuildConfiguredCursorPath(action, scopeName, configuredScope);
        }

        throw new InvalidOperationException(
            $"Checkpoint scope could not be resolved for cursor '{checkpointIdentity}'. Project-scoped checkpoint operations require both action and endpoint context. {BuildScopeDiagnosticMessage()}");
    }

    private string ResolveContinuationKey(string checkpointIdentity)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedScopeName) &&
            TryResolveLiveEndpointScope(out var endpointUrl, out var orgFolder, out var projectName))
        {
            return string.IsNullOrWhiteSpace(endpointUrl)
                ? $"{orgFolder}/{projectName}/.migration/{parsedAction.ToLowerInvariant()}.{parsedScopeName.ToLowerInvariant()}.continuation.json"
                : PackagePaths.ContinuationFile(parsedAction, parsedScopeName, endpointUrl, projectName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveLiveEndpointScope(out var configEndpointUrl, out var configOrgFolder, out var configProjectName))
        {
            return string.IsNullOrWhiteSpace(configEndpointUrl)
                ? $"{configOrgFolder}/{configProjectName}/.migration/{configAction.ToLowerInvariant()}.{checkpointIdentity.ToLowerInvariant()}.continuation.json"
                : PackagePaths.ContinuationFile(configAction, checkpointIdentity, configEndpointUrl, configProjectName);
        }

        if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
            TryResolveSingleConfiguredProjectScope(out var configuredScope))
        {
            return BuildConfiguredContinuationPath(action, scopeName, configuredScope);
        }

        if (!checkpointIdentity.Contains("."))
        {
            return PackagePaths.ContinuationFile(checkpointIdentity);
        }

        throw new InvalidOperationException(
            $"Checkpoint scope could not be resolved for continuation token '{checkpointIdentity}'. Project-scoped checkpoint operations require both action and endpoint context.");
    }

    private bool TryResolveEndpoint(out string endpointUrl, out string projectName)
    {
        if (TryResolveLiveEndpointScope(out endpointUrl, out _, out projectName) &&
            !string.IsNullOrWhiteSpace(endpointUrl))
        {
            return true;
        }

        endpointUrl = string.Empty;
        projectName = string.Empty;
        return false;
    }

    private bool TryResolveLiveEndpointScope(out string endpointUrl, out string orgFolder, out string projectName)
    {
        if (TryResolveLiveEndpointScope(_currentJobEndpointAccessor?.Source, out endpointUrl, out orgFolder, out projectName))
        {
            return true;
        }

        if (TryResolveLiveEndpointScope(_currentJobEndpointAccessor?.Target, out endpointUrl, out orgFolder, out projectName))
        {
            return true;
        }

        endpointUrl = string.Empty;
        orgFolder = string.Empty;
        projectName = string.Empty;
        return false;
    }

    private static bool TryResolveLiveEndpointScope(
        ISourceEndpointInfo? endpoint,
        out string endpointUrl,
        out string orgFolder,
        out string projectName)
        => TryResolveLiveEndpointScope(
            endpoint?.Url,
            endpoint?.ConnectorType,
            endpoint?.Project,
            out endpointUrl,
            out orgFolder,
            out projectName);

    private static bool TryResolveLiveEndpointScope(
        ITargetEndpointInfo? endpoint,
        out string endpointUrl,
        out string orgFolder,
        out string projectName)
        => TryResolveLiveEndpointScope(
            endpoint?.Url,
            endpoint?.ConnectorType,
            endpoint?.Project,
            out endpointUrl,
            out orgFolder,
            out projectName);

    private static bool TryResolveLiveEndpointScope(
        string? endpointUrlValue,
        string? connectorType,
        string? project,
        out string endpointUrl,
        out string orgFolder,
        out string projectName)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            endpointUrl = endpointUrlValue ?? string.Empty;
            projectName = project!;
            orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
                ? PackagePathResolver.Sanitise((connectorType ?? "Unknown").ToLowerInvariant())
                : PackagePathResolver.ExtractOrgFolderName(endpointUrl);
            return true;
        }

        endpointUrl = string.Empty;
        orgFolder = string.Empty;
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

    private bool TryResolveActionAndScopeName(string checkpointIdentity, out string action, out string scopeName)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out action, out scopeName))
            return true;

        if (TryResolveActionFromConfig(out action))
        {
            scopeName = checkpointIdentity;
            return true;
        }

        action = string.Empty;
        scopeName = string.Empty;
        return false;
    }

    private bool TryResolveConfiguredProjectScopes(out IReadOnlyList<ConfiguredProjectScope> scopes)
    {
        var config = _currentPackageConfigAccessor?.Current;
        if (config is null)
        {
            scopes = Array.Empty<ConfiguredProjectScope>();
            return false;
        }

        var resolved = new List<ConfiguredProjectScope>();
        var configuredOrganisations = config.GetSection("MigrationPlatform:Organisations").GetChildren().ToList();

        foreach (var organisation in configuredOrganisations)
        {
            var enabled = organisation["Enabled"];
            if (enabled?.Equals("false", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            var projects = organisation
                .GetSection("Projects")
                .GetChildren()
                .Select(project => project.Value)
                .Where(project => !string.IsNullOrWhiteSpace(project))
                .Select(project => project!)
                .ToList();

            if (projects.Count == 0)
                continue;

            var endpointUrl = organisation["Url"] ?? organisation["Collection"] ?? string.Empty;
            var orgType = organisation["Type"] ?? "Unknown";
            var orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
                ? PackagePathResolver.Sanitise(orgType.ToLowerInvariant())
                : PackagePathResolver.ExtractOrgFolderName(endpointUrl);

            foreach (var project in projects)
            {
                resolved.Add(new ConfiguredProjectScope(endpointUrl, orgFolder, project));
            }
        }

        if (resolved.Count == 0)
        {
            if (TryResolveConfiguredModeProjectScope(config, out var modeScope))
            {
                resolved.Add(modeScope);
            }
        }

        scopes = resolved;
        return resolved.Count > 0;
    }

    private bool TryResolveConfiguredModeProjectScope(IConfiguration config, out ConfiguredProjectScope scope)
    {
        if (TryResolveActionFromConfig(out var action))
        {
            var preferredSection = action is "import" or "prepare" or "validate"
                ? "MigrationPlatform:Target"
                : "MigrationPlatform:Source";

            if (TryResolveConfiguredEndpointProjectScope(config, preferredSection, out scope))
            {
                return true;
            }

            var fallbackSection = preferredSection == "MigrationPlatform:Source"
                ? "MigrationPlatform:Target"
                : "MigrationPlatform:Source";

            if (TryResolveConfiguredEndpointProjectScope(config, fallbackSection, out scope))
            {
                return true;
            }
        }

        if (TryResolveConfiguredEndpointProjectScope(config, "MigrationPlatform:Source", out scope))
        {
            return true;
        }

        return TryResolveConfiguredEndpointProjectScope(config, "MigrationPlatform:Target", out scope);
    }

    private static bool TryResolveConfiguredEndpointProjectScope(
        IConfiguration config,
        string sectionPath,
        out ConfiguredProjectScope scope)
    {
        var project = config[$"{sectionPath}:Project"];
        if (string.IsNullOrWhiteSpace(project))
        {
            scope = null!;
            return false;
        }

        var endpointUrl = config[$"{sectionPath}:Url"] ?? config[$"{sectionPath}:Collection"] ?? string.Empty;
        var endpointType = config[$"{sectionPath}:Type"] ?? "Unknown";
        var orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
            ? PackagePathResolver.Sanitise(endpointType.ToLowerInvariant())
            : PackagePathResolver.ExtractOrgFolderName(endpointUrl);

        scope = new ConfiguredProjectScope(endpointUrl, orgFolder, project!);
        return true;
    }

    private bool TryResolveSingleConfiguredProjectScope(out ConfiguredProjectScope scope)
    {
        if (TryResolveConfiguredProjectScopes(out var scopes) && scopes.Count == 1)
        {
            scope = scopes[0];
            return true;
        }

        scope = null!;
        return false;
    }

    private static string BuildConfiguredCursorPath(string action, string scopeName, ConfiguredProjectScope scope)
        => string.IsNullOrWhiteSpace(scope.EndpointUrl)
            ? $"{scope.OrgFolder}/{scope.ProjectName}/.migration/{action.ToLowerInvariant()}.{scopeName.ToLowerInvariant()}.cursor.json"
            : PackagePaths.CursorFile(action, scopeName, scope.EndpointUrl, scope.ProjectName);

    private static string BuildConfiguredContinuationPath(string action, string scopeName, ConfiguredProjectScope scope)
        => string.IsNullOrWhiteSpace(scope.EndpointUrl)
            ? $"{scope.OrgFolder}/{scope.ProjectName}/.migration/{action.ToLowerInvariant()}.{scopeName.ToLowerInvariant()}.continuation.json"
            : PackagePaths.ContinuationFile(action, scopeName, scope.EndpointUrl, scope.ProjectName);

    private string BuildScopeDiagnosticMessage()
    {
        var source = _currentJobEndpointAccessor?.Source;
        var target = _currentJobEndpointAccessor?.Target;
        var config = _currentPackageConfigAccessor?.Current;

        return
            $"LiveSource(Type='{source?.ConnectorType ?? "<null>"}', Url='{source?.Url ?? "<null>"}', Project='{source?.Project ?? "<null>"}'); " +
            $"LiveTarget(Type='{target?.ConnectorType ?? "<null>"}', Url='{target?.Url ?? "<null>"}', Project='{target?.Project ?? "<null>"}'); " +
            $"ConfigMode='{config?["MigrationPlatform:Mode"] ?? config?["Mode"] ?? "<null>"}'; " +
            $"ConfigSource(Type='{config?["MigrationPlatform:Source:Type"] ?? "<null>"}', Url='{config?["MigrationPlatform:Source:Url"] ?? "<null>"}', Project='{config?["MigrationPlatform:Source:Project"] ?? "<null>"}'); " +
            $"ConfigTarget(Type='{config?["MigrationPlatform:Target:Type"] ?? "<null>"}', Url='{config?["MigrationPlatform:Target:Url"] ?? "<null>"}', Project='{config?["MigrationPlatform:Target:Project"] ?? "<null>"}').";
    }

    private sealed record ConfiguredProjectScope(string EndpointUrl, string OrgFolder, string ProjectName);
}
