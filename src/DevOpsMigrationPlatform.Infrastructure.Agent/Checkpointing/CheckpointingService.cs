// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class CheckpointingService : ICheckpointingService
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);
    private readonly ICurrentJobEndpointAccessor? _currentJobEndpointAccessor;
    private readonly ICurrentPackageConfigAccessor? _currentPackageConfigAccessor;
    private readonly ILogger<CheckpointingService>? _logger;
    private readonly IPackageAccess? _package;

    public CheckpointingService(
        ICurrentJobEndpointAccessor? currentJobEndpointAccessor = null,
        ICurrentPackageConfigAccessor? currentPackageConfigAccessor = null,
        ILogger<CheckpointingService>? logger = null,
        IPackageAccess? package = null)
    {
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _currentPackageConfigAccessor = currentPackageConfigAccessor;
        _logger = logger;
        _package = package;
    }

    public async Task<CursorEntry?> ReadCursorAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "read");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var contexts = ResolveCursorReadContexts(checkpointIdentity);
        if (contexts.Count > 0)
        {
            activity?.SetTag("cursor.action", contexts[0].Action);
            activity?.SetTag("cursor.module", contexts[0].Module);
        }

        foreach (var context in contexts)
        {
            _logger?.LogDebug(
                "Reading cursor for {Action}.{Module} at scope org='{Org}' project='{Project}'.",
                context.Action,
                context.Module,
                context.Organisation ?? "<migration>",
                context.Project ?? "<none>");

            var result = await ResolvePackage().RequestMetaAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.Payload is null)
                continue;

            var json = await ReadUtf8Async(result.Payload.Content, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CursorEntry>(json);
        }

        return null;
    }

    public async Task WriteCursorAsync(string checkpointIdentity, CursorEntry cursor, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var context = ResolveCursorWriteContext(checkpointIdentity);
        activity?.SetTag("cursor.action", context.Action);
        activity?.SetTag("cursor.module", context.Module);
        _logger?.LogDebug("Writing cursor for {Action}.{Module}.", context.Action, context.Module);

        var json = JsonSerializer.Serialize(cursor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ResolvePackage().PersistMetaAsync(context, new PackageMetaPayload(stream), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        if (!TryResolveCursorDeleteContext(checkpointIdentity, out var context))
            return;

        _logger?.LogDebug("Deleting cursor for {Action}.{Module}.", context.Action, context.Module);
        await ResolvePackage().ResetMetaAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<BatchContinuationToken?> ReadContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.continuation.update");
        activity?.SetTag("operation", "read");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var contexts = ResolveContinuationReadContexts(checkpointIdentity);
        if (contexts.Count > 0)
        {
            activity?.SetTag("continuation.action", contexts[0].Action);
            activity?.SetTag("continuation.module", contexts[0].Module);
        }

        foreach (var context in contexts)
        {
            var result = await ResolvePackage().RequestMetaAsync(context, cancellationToken).ConfigureAwait(false);
            if (result.Payload is null)
                continue;

            var json = await ReadUtf8Async(result.Payload.Content, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<BatchContinuationToken>(json);
        }

        return null;
    }

    public async Task WriteContinuationTokenAsync(string checkpointIdentity, BatchContinuationToken token, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.continuation.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var context = ResolveContinuationWriteContext(checkpointIdentity);
        activity?.SetTag("continuation.action", context.Action);
        activity?.SetTag("continuation.module", context.Module);

        var json = JsonSerializer.Serialize(token);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ResolvePackage().PersistMetaAsync(context, new PackageMetaPayload(stream), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        if (!TryResolveContinuationDeleteContext(checkpointIdentity, out var context))
            return;

        await ResolvePackage().ResetMetaAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<PackageMetaContext> ResolveCursorReadContexts(string checkpointIdentity)
        => ResolveReadContexts(PackageMetaKind.CheckpointCursor, checkpointIdentity);

    private IReadOnlyList<PackageMetaContext> ResolveContinuationReadContexts(string checkpointIdentity)
        => ResolveReadContexts(PackageMetaKind.ContinuationToken, checkpointIdentity);

    private PackageMetaContext ResolveCursorWriteContext(string checkpointIdentity)
        => ResolveWriteContext(PackageMetaKind.CheckpointCursor, checkpointIdentity);

    private PackageMetaContext ResolveContinuationWriteContext(string checkpointIdentity)
        => ResolveWriteContext(PackageMetaKind.ContinuationToken, checkpointIdentity);

    private bool TryResolveCursorDeleteContext(string checkpointIdentity, out PackageMetaContext context)
        => TryResolveDeleteContext(PackageMetaKind.CheckpointCursor, checkpointIdentity, out context);

    private bool TryResolveContinuationDeleteContext(string checkpointIdentity, out PackageMetaContext context)
        => TryResolveDeleteContext(PackageMetaKind.ContinuationToken, checkpointIdentity, out context);

    private IReadOnlyList<PackageMetaContext> ResolveReadContexts(PackageMetaKind kind, string checkpointIdentity)
    {
        var (action, module) = ResolveActionAndModuleOrThrow(checkpointIdentity);
        var scopes = ResolveReadScopes();
        return scopes
            .Select(scope => BuildMetaContext(kind, action, module, scope))
            .ToArray();
    }

    private PackageMetaContext ResolveWriteContext(PackageMetaKind kind, string checkpointIdentity)
    {
        var (action, module) = ResolveActionAndModuleOrThrow(checkpointIdentity);
        var scope = ResolveWriteScope();
        return BuildMetaContext(kind, action, module, scope);
    }

    private bool TryResolveDeleteContext(PackageMetaKind kind, string checkpointIdentity, out PackageMetaContext context)
    {
        if (!TryResolveActionAndModule(checkpointIdentity, out var action, out var module))
        {
            context = null!;
            return false;
        }

        var scope = ResolveWriteScope();
        context = BuildMetaContext(kind, action, module, scope);
        return true;
    }

    private (string Action, string Module) ResolveActionAndModuleOrThrow(string checkpointIdentity)
    {
        if (TryResolveActionAndModule(checkpointIdentity, out var action, out var module))
            return (action, module);

        throw new InvalidOperationException(
            $"Checkpoint identity '{checkpointIdentity}' does not include an action and no action could be resolved from package config. {BuildScopeDiagnosticMessage()}");
    }

    private bool TryResolveActionAndModule(string checkpointIdentity, out string action, out string module)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedModule))
        {
            action = parsedAction;
            module = parsedModule;
            return true;
        }

        if (TryResolveActionFromConfig(out var configAction))
        {
            action = configAction;
            module = checkpointIdentity;
            return true;
        }

        action = string.Empty;
        module = string.Empty;
        return false;
    }

    private PackageMetaContext BuildMetaContext(
        PackageMetaKind kind,
        string action,
        string module,
        ScopeResolution scope)
        => new(
            kind,
            Organisation: scope.Organisation,
            Project: scope.Project,
            Action: action,
            Module: module);

    private IReadOnlyList<ScopeResolution> ResolveReadScopes()
    {
        var resolved = new List<ScopeResolution>();

        if (TryResolveLiveProjectScope(out var liveProject))
        {
            resolved.Add(liveProject);
            resolved.Add(new ScopeResolution(liveProject.Organisation, null));
            resolved.Add(ScopeResolution.Migration);
            return DistinctScopes(resolved);
        }

        if (TryResolveLiveOrgScope(out var liveOrg))
        {
            resolved.Add(liveOrg);
            resolved.Add(ScopeResolution.Migration);
            return DistinctScopes(resolved);
        }

        if (TryResolveSingleConfiguredProjectScope(out var configuredProject))
        {
            resolved.Add(new ScopeResolution(configuredProject.OrgFolder, configuredProject.ProjectName));
            resolved.Add(new ScopeResolution(configuredProject.OrgFolder, null));
            resolved.Add(ScopeResolution.Migration);
            return DistinctScopes(resolved);
        }

        if (TryResolveConfiguredOrgScope(out var configuredOrg))
        {
            resolved.Add(configuredOrg);
            resolved.Add(ScopeResolution.Migration);
            return DistinctScopes(resolved);
        }

        return [ScopeResolution.Migration];
    }

    private ScopeResolution ResolveWriteScope()
    {
        if (TryResolveLiveProjectScope(out var liveProject))
            return liveProject;

        if (TryResolveLiveOrgScope(out var liveOrg))
            return liveOrg;

        if (TryResolveSingleConfiguredProjectScope(out var configuredProject))
            return new ScopeResolution(configuredProject.OrgFolder, configuredProject.ProjectName);

        if (TryResolveConfiguredOrgScope(out var configuredOrg))
            return configuredOrg;

        return ScopeResolution.Migration;
    }

    private static IReadOnlyList<ScopeResolution> DistinctScopes(IEnumerable<ScopeResolution> scopes)
        => scopes.Distinct().ToArray();

    private bool TryResolveLiveProjectScope(out ScopeResolution scope)
    {
        if (TryResolveLiveProjectScope(_currentJobEndpointAccessor?.Source, out scope))
            return true;

        return TryResolveLiveProjectScope(_currentJobEndpointAccessor?.Target, out scope);
    }

    private static bool TryResolveLiveProjectScope(ISourceEndpointInfo? endpoint, out ScopeResolution scope)
        => TryResolveLiveProjectScope(endpoint?.Url, endpoint?.ConnectorType, endpoint?.Project, out scope);

    private static bool TryResolveLiveProjectScope(ITargetEndpointInfo? endpoint, out ScopeResolution scope)
        => TryResolveLiveProjectScope(endpoint?.Url, endpoint?.ConnectorType, endpoint?.Project, out scope);

    private static bool TryResolveLiveProjectScope(string? endpointUrlValue, string? connectorType, string? project, out ScopeResolution scope)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            var endpointUrl = endpointUrlValue ?? string.Empty;
            var orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
                ? PackagePathResolver.Sanitise((connectorType ?? "Unknown").ToLowerInvariant())
                : PackagePathResolver.ExtractOrgFolderName(endpointUrl);
            scope = new ScopeResolution(orgFolder, project);
            return true;
        }

        scope = default;
        return false;
    }

    private bool TryResolveLiveOrgScope(out ScopeResolution scope)
    {
        if (TryResolveLiveOrgScope(_currentJobEndpointAccessor?.Source, out scope))
            return true;

        return TryResolveLiveOrgScope(_currentJobEndpointAccessor?.Target, out scope);
    }

    private static bool TryResolveLiveOrgScope(ISourceEndpointInfo? endpoint, out ScopeResolution scope)
        => TryResolveLiveOrgScope(endpoint?.Url, endpoint?.ConnectorType, out scope);

    private static bool TryResolveLiveOrgScope(ITargetEndpointInfo? endpoint, out ScopeResolution scope)
        => TryResolveLiveOrgScope(endpoint?.Url, endpoint?.ConnectorType, out scope);

    private static bool TryResolveLiveOrgScope(string? endpointUrlValue, string? connectorType, out ScopeResolution scope)
    {
        if (!string.IsNullOrWhiteSpace(endpointUrlValue))
        {
            scope = new ScopeResolution(PackagePathResolver.ExtractOrgFolderName(endpointUrlValue!), null);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(connectorType))
        {
            scope = new ScopeResolution(PackagePathResolver.Sanitise(connectorType!.ToLowerInvariant()), null);
            return true;
        }

        scope = default;
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

        if (resolved.Count == 0 && TryResolveConfiguredModeProjectScope(config, out var modeScope))
        {
            resolved.Add(modeScope);
        }

        scopes = resolved;
        return resolved.Count > 0;
    }

    private bool TryResolveConfiguredOrgScope(out ScopeResolution scope)
    {
        var config = _currentPackageConfigAccessor?.Current;
        if (config is null)
        {
            scope = default;
            return false;
        }

        var orgFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuredOrganisations = config.GetSection("MigrationPlatform:Organisations").GetChildren().ToList();
        foreach (var organisation in configuredOrganisations)
        {
            var enabled = organisation["Enabled"];
            if (enabled?.Equals("false", StringComparison.OrdinalIgnoreCase) == true)
                continue;

            var endpointUrl = organisation["Url"] ?? organisation["Collection"] ?? string.Empty;
            var orgType = organisation["Type"] ?? "Unknown";
            var orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
                ? PackagePathResolver.Sanitise(orgType.ToLowerInvariant())
                : PackagePathResolver.ExtractOrgFolderName(endpointUrl);
            orgFolders.Add(orgFolder);
        }

        if (orgFolders.Count == 1)
        {
            scope = new ScopeResolution(orgFolders.Single(), null);
            return true;
        }

        if (TryResolveConfiguredEndpointOrgScope(config, "MigrationPlatform:Source", out scope))
            return true;

        return TryResolveConfiguredEndpointOrgScope(config, "MigrationPlatform:Target", out scope);
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

    private static bool TryResolveConfiguredEndpointOrgScope(
        IConfiguration config,
        string sectionPath,
        out ScopeResolution scope)
    {
        var endpointUrl = config[$"{sectionPath}:Url"] ?? config[$"{sectionPath}:Collection"] ?? string.Empty;
        var endpointType = config[$"{sectionPath}:Type"] ?? "Unknown";
        if (string.IsNullOrWhiteSpace(endpointUrl) && string.IsNullOrWhiteSpace(endpointType))
        {
            scope = default;
            return false;
        }

        var orgFolder = string.IsNullOrWhiteSpace(endpointUrl)
            ? PackagePathResolver.Sanitise(endpointType.ToLowerInvariant())
            : PackagePathResolver.ExtractOrgFolderName(endpointUrl);

        scope = new ScopeResolution(orgFolder, null);
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

    private IPackageAccess ResolvePackage()
        => _package ?? throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for checkpoint state operations.");

    private readonly record struct ScopeResolution(string? Organisation, string? Project)
    {
        public static ScopeResolution Migration { get; } = new(null, null);
    }

    private sealed record ConfiguredProjectScope(string EndpointUrl, string OrgFolder, string ProjectName);

    private static async Task<string> ReadUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(stream);
        cancellationToken.ThrowIfCancellationRequested();
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }
}
