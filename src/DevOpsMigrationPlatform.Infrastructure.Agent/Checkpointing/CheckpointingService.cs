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
        var context = ResolveCursorContext(checkpointIdentity);
        activity?.SetTag("cursor.action", context.Action);
        activity?.SetTag("cursor.module", context.Module);
        _logger?.LogDebug("Reading cursor for {Action}.{Module}.", context.Action, context.Module);

        var result = await ResolvePackage().RequestMetaAsync(context, cancellationToken).ConfigureAwait(false);
        if (result.Payload is null)
            return null;

        var json = await ReadUtf8Async(result.Payload.Content, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(string checkpointIdentity, CursorEntry cursor, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.cursor.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var context = ResolveCursorContext(checkpointIdentity);
        activity?.SetTag("cursor.action", context.Action);
        activity?.SetTag("cursor.module", context.Module);
        _logger?.LogDebug("Writing cursor for {Action}.{Module}.", context.Action, context.Module);

        var json = JsonSerializer.Serialize(cursor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ResolvePackage().PersistMetaAsync(context, new PackageMetaPayload(stream), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        foreach (var context in ResolveCursorDeleteContexts(checkpointIdentity))
        {
            _logger?.LogDebug("Deleting cursor for {Action}.{Module}.", context.Action, context.Module);
            await ResolvePackage().ResetMetaAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BatchContinuationToken?> ReadContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.continuation.update");
        activity?.SetTag("operation", "read");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var context = ResolveContinuationContext(checkpointIdentity);
        activity?.SetTag("continuation.action", context.Action);
        activity?.SetTag("continuation.module", context.Module);

        var result = await ResolvePackage().RequestMetaAsync(context, cancellationToken).ConfigureAwait(false);
        if (result.Payload is null)
            return null;

        var json = await ReadUtf8Async(result.Payload.Content, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<BatchContinuationToken>(json);
    }

    public async Task WriteContinuationTokenAsync(string checkpointIdentity, BatchContinuationToken token, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("state.continuation.update");
        activity?.SetTag("operation", "write");
        activity?.SetTag("checkpoint.identity", checkpointIdentity);
        var context = ResolveContinuationContext(checkpointIdentity);
        activity?.SetTag("continuation.action", context.Action);
        activity?.SetTag("continuation.module", context.Module);

        var json = JsonSerializer.Serialize(token);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ResolvePackage().PersistMetaAsync(context, new PackageMetaPayload(stream), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken)
    {
        foreach (var context in ResolveContinuationDeleteContexts(checkpointIdentity))
        {
            await ResolvePackage().ResetMetaAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private IEnumerable<PackageMetaContext> ResolveCursorDeleteContexts(string checkpointIdentity)
        => ResolveDeleteContexts(checkpointIdentity, ResolveCursorContext, PackageMetaKind.CheckpointCursor);

    private IEnumerable<PackageMetaContext> ResolveContinuationDeleteContexts(string checkpointIdentity)
        => ResolveDeleteContexts(checkpointIdentity, ResolveContinuationContext, PackageMetaKind.ContinuationToken);

    private IEnumerable<PackageMetaContext> ResolveDeleteContexts(
        string checkpointIdentity,
        Func<string, PackageMetaContext> directResolver,
        PackageMetaKind kind)
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
                    .Select(_ => new PackageMetaContext(kind, Action: action, Module: scopeName))
                    .Distinct()
                    .ToArray();
            }

            return Array.Empty<PackageMetaContext>();
        }
    }

    private PackageMetaContext ResolveCursorContext(string checkpointIdentity)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedScopeName) &&
            TryResolveLiveEndpointScope(out _, out _, out _))
        {
            return new PackageMetaContext(PackageMetaKind.CheckpointCursor, Action: parsedAction, Module: parsedScopeName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveLiveEndpointScope(out _, out _, out _))
        {
            return new PackageMetaContext(PackageMetaKind.CheckpointCursor, Action: configAction, Module: checkpointIdentity);
        }

        if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
            TryResolveSingleConfiguredProjectScope(out _))
        {
            return new PackageMetaContext(PackageMetaKind.CheckpointCursor, Action: action, Module: scopeName);
        }

        throw new InvalidOperationException(
            $"Checkpoint scope could not be resolved for cursor '{checkpointIdentity}'. Project-scoped checkpoint operations require both action and endpoint context. {BuildScopeDiagnosticMessage()}");
    }

    private PackageMetaContext ResolveContinuationContext(string checkpointIdentity)
    {
        if (StateCursorIdentity.TryParse(checkpointIdentity, out var parsedAction, out var parsedScopeName) &&
            TryResolveLiveEndpointScope(out _, out _, out _))
        {
            return new PackageMetaContext(PackageMetaKind.ContinuationToken, Action: parsedAction, Module: parsedScopeName);
        }

        if (TryResolveActionFromConfig(out var configAction) &&
            TryResolveLiveEndpointScope(out _, out _, out _))
        {
            return new PackageMetaContext(PackageMetaKind.ContinuationToken, Action: configAction, Module: checkpointIdentity);
        }

        if (TryResolveActionAndScopeName(checkpointIdentity, out var action, out var scopeName) &&
            TryResolveSingleConfiguredProjectScope(out _))
        {
            return new PackageMetaContext(PackageMetaKind.ContinuationToken, Action: action, Module: scopeName);
        }

        throw new InvalidOperationException(
            $"Checkpoint scope could not be resolved for continuation token '{checkpointIdentity}'. Project-scoped checkpoint operations require both action and endpoint context.");
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

        if (resolved.Count == 0 && TryResolveConfiguredModeProjectScope(config, out var modeScope))
        {
            resolved.Add(modeScope);
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
