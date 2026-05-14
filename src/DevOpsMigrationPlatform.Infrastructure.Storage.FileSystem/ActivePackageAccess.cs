// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
#if !NET481
using System.Diagnostics.Metrics;
#endif
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;

internal sealed class ActivePackageAccess : IPackageAccess
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);
#if !NET481
    private static readonly Meter s_meter = new(WellKnownMeterNames.Agent, "4.0");
    private static readonly Counter<long> s_operationCounter = s_meter.CreateCounter<long>(WellKnownAgentMetricNames.PackageBoundaryOperations, unit: "{operation}");
    private static readonly Counter<long> s_errorCounter = s_meter.CreateCounter<long>(WellKnownAgentMetricNames.PackageBoundaryErrors, unit: "{error}");
    private static readonly Histogram<double> s_durationHistogram = s_meter.CreateHistogram<double>(WellKnownAgentMetricNames.PackageBoundaryDurationMs, unit: "ms");
#endif

    private readonly ActivePackageState _activePackageState;
    private readonly PackagePathRouter _router;
    private readonly IControlPlaneAgentClient? _controlPlaneAgentClient;
    private readonly Guid _agentInstanceId;
    private readonly ILogger<ActivePackageAccess>? _logger;

    [ActivatorUtilitiesConstructor]
    public ActivePackageAccess(
        ActivePackageState activePackageState,
        PackagePathRouter router,
        IControlPlaneAgentClient? controlPlaneAgentClient = null,
        AgentInstanceIdHolder? agentInstanceIdHolder = null,
        ILogger<ActivePackageAccess>? logger = null)
    {
        _activePackageState = activePackageState ?? throw new ArgumentNullException(nameof(activePackageState));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _controlPlaneAgentClient = controlPlaneAgentClient;
        _agentInstanceId = agentInstanceIdHolder?.AgentInstanceId ?? Guid.Empty;
        _logger = logger;
    }

    public ActivePackageAccess(
        ActivePackageState activePackageState,
        PackagePathRouter router,
        ILogger<ActivePackageAccess> logger)
        : this(activePackageState, router, null, null, logger)
    {
    }

    public async ValueTask<PackagePayload?> RequestContentAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        return await ObserveAsync(
            "request",
            path,
            async () =>
            {
                var content = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                if (content is null)
                    return null;

                return new PackagePayload(
                    new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false),
                    "application/json");
            }).ConfigureAwait(false);
    }

    public async ValueTask<bool> ContentExistsAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        return await ObserveAsync(
            "exists",
            path,
            async () => await store.ExistsAsync(path, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> EnumerateContentAsync(
        PackageContentContext context,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);

        await foreach (var item in store.EnumerateAsync(path, cancellationToken).ConfigureAwait(false))
            yield return item;
    }

    public async ValueTask<Stream?> RequestContentBinaryAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        return await ObserveAsync(
            "request-binary",
            path,
            async () =>
            {
                var stream = await store.ReadBinaryAsync(path, cancellationToken).ConfigureAwait(false);
                return stream;
            }).ConfigureAwait(false);
    }

    public async ValueTask<PackageMetaResult> RequestMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveMetaPath(context);
        return await ObserveAsync(
            "request-meta",
            path,
            async () =>
            {
                var content = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
                PackageMetaPayload? payload = content is null
                    ? null
                    : new PackageMetaPayload(
                        new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false),
                        "application/json");
                return new PackageMetaResult(path, payload);
            },
            context.Kind.ToString()).ConfigureAwait(false);
    }

    public ValueTask<System.Data.Common.DbConnection> OpenNativeDatabaseAsync(
        PackageMetaKind kind,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localRoot = RequireLocalRoot();
        var nativePath = _router.ResolveNativePath(kind, localRoot);
        var dir = System.IO.Path.GetDirectoryName(nativePath)!;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);
        var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={nativePath}");
        return new ValueTask<System.Data.Common.DbConnection>(connection);
    }

    public ValueTask<IDisposable> AcquireLockAsync(
        string jobId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localRoot = RequireLocalRoot();
        var lockFilePath = _router.ResolveLockPath(localRoot);
        var dir = System.IO.Path.GetDirectoryName(lockFilePath)!;
        if (!System.IO.Directory.Exists(dir))
            System.IO.Directory.CreateDirectory(dir);

        return TryAcquireAsync(lockFilePath, localRoot, jobId, cancellationToken);
    }

    public async ValueTask PersistContentAsync(
        PackageContentContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        await ObserveAsync(
            "persist",
            path,
            async () =>
            {
                var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
                await store.WriteAsync(path, content, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }

    public async ValueTask PersistContentStreamAsync(
        PackageContentContext context,
        Stream payload,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        await ObserveAsync(
            "persist-stream",
            path,
            async () =>
            {
                await store.WriteStreamAsync(path, payload, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }

    public async ValueTask PersistMetaAsync(
        PackageMetaContext context,
        PackageMetaPayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var authoritativePath = _router.ResolveMetaPath(context);
        await ObserveAsync(
            "persist-meta",
            authoritativePath,
            async () =>
            {
                var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
                await store.WriteAsync(authoritativePath, content, cancellationToken).ConfigureAwait(false);

                if (context.RelatedToRun && !string.IsNullOrWhiteSpace(_activePackageState.CurrentRunId))
                {
                    var runAuditPath = _router.ResolveMetaPath(context, _activePackageState.CurrentRunId, runAudit: true);
                    await store.WriteAsync(runAuditPath, content, cancellationToken).ConfigureAwait(false);
                }

                return true;
            },
            context.Kind.ToString()).ConfigureAwait(false);
    }

    public async ValueTask DeleteMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStateStore();
        var path = _router.ResolveMetaPath(context);
        await ObserveAsync(
            "delete-meta",
            path,
            async () =>
            {
                await store.DeleteAsync(path, cancellationToken).ConfigureAwait(false);
                return true;
            },
            context.Kind.ToString()).ConfigureAwait(false);
    }

    public async ValueTask AppendContentAsync(
        PackageContentContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        await ObserveAsync(
            "append",
            path,
            async () =>
            {
                var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(path, content, cancellationToken).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
    }

    public async ValueTask AppendLogAsync(
        PackageLogContext context,
        PackageLogPayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveLogPath(context);
        await ObserveAsync(
            "append-log",
            path,
            async () =>
            {
                var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
                await store.AppendAsync(path, content, cancellationToken).ConfigureAwait(false);
                return true;
            },
            stream: context.Stream.ToString()).ConfigureAwait(false);
    }

    private IArtefactStore RequireStore()
        => _activePackageState.CurrentStore
            ?? throw new PackageOperationException(
                "PKG_STORE_UNAVAILABLE",
                "No active package store is available.");

    private IStateStore RequireStateStore()
        => _activePackageState.CurrentStateStore
            ?? throw new PackageOperationException(
                "PKG_STATE_STORE_UNAVAILABLE",
                "No active package state store is available.");

    private string RequireLocalRoot()
    {
        var packageUri = _activePackageState.CurrentJob?.Package?.PackageUri
            ?? throw new PackageOperationException("PKG_STORE_UNAVAILABLE", "No active package store is available.");
        if (packageUri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return System.IO.Path.GetFullPath(packageUri.Substring("file:///".Length).Replace('/', System.IO.Path.DirectorySeparatorChar));
        return System.IO.Path.GetFullPath(packageUri);
    }

    private static async Task<string> ReadUtf8Async(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
            stream.Position = 0;

        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true);
        cancellationToken.ThrowIfCancellationRequested();
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return content;
    }

    private async ValueTask<T> ObserveAsync<T>(
        string operation,
        string path,
        Func<ValueTask<T>> action,
        string? kind = null,
        string? stream = null)
    {
        using var activity = s_activitySource.StartActivity($"package.boundary.{operation}");
        activity?.SetTag("package.operation", operation);
        activity?.SetTag("package.path", path);
        activity?.SetTag("package.kind", kind);
        activity?.SetTag("package.stream", stream);
        activity?.SetTag("job.id", _activePackageState.CurrentJob?.JobId);
        activity?.SetTag("run.id", _activePackageState.CurrentRunId);

        var startedAt = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            activity?.SetTag("package.result", "success");
#if !NET481
            s_operationCounter.Add(1, BuildTags(operation, path, kind, stream, "success", null));
#endif
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("package.result", "error");
            activity?.SetTag("error.type", ex.GetType().Name);
#if !NET481
            s_errorCounter.Add(1, BuildTags(operation, path, kind, stream, "error", ex.GetType().Name));
#endif
            throw;
        }
        finally
        {
            startedAt.Stop();
            activity?.SetTag("package.duration.ms", startedAt.Elapsed.TotalMilliseconds);
#if !NET481
            s_durationHistogram.Record(startedAt.Elapsed.TotalMilliseconds, BuildTags(operation, path, kind, stream, null, null));
#endif
        }
    }

#if !NET481
    private TagList BuildTags(
        string operation,
        string path,
        string? kind,
        string? stream,
        string? result,
        string? errorType)
    {
        TagList tags = new()
        {
            { "operation", operation },
            { "path", path },
            { "kind", kind },
            { "stream", stream },
            { "run.id", _activePackageState.CurrentRunId },
            { "job.id", _activePackageState.CurrentJob?.JobId }
        };

        if (!string.IsNullOrWhiteSpace(result))
            tags.Add("result", result);
        if (!string.IsNullOrWhiteSpace(errorType))
            tags.Add("error.type", errorType);

        return tags;
    }
#endif

    private async ValueTask<IDisposable> TryAcquireAsync(
        string lockFilePath,
        string packagePath,
        string jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var fs = new FileStream(
                lockFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            var lockContent = JsonSerializer.Serialize(new
            {
                jobId,
                agentInstanceId = _agentInstanceId.ToString(),
                acquiredAt = DateTimeOffset.UtcNow.ToString("O")
            });

            using var writer = new StreamWriter(fs);
            await writer.WriteAsync(lockContent).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);

            _logger?.LogInformation(
                "[PackageLock] Lock acquired for job {JobId} by agent {AgentInstanceId} at {Path}",
                jobId, _agentInstanceId, lockFilePath);

            return new PackageLockHandle(lockFilePath, _logger);
        }
        catch (IOException)
        {
            return await HandleExistingLockAsync(lockFilePath, packagePath, jobId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<IDisposable> HandleExistingLockAsync(
        string lockFilePath,
        string packagePath,
        string jobId,
        CancellationToken cancellationToken)
    {
        string? existingContent = null;
        try
        {
            existingContent = File.ReadAllText(lockFilePath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[PackageLock] Could not read existing lock file at {Path} — treating as stale.",
                lockFilePath);
        }

        if (existingContent is not null)
        {
            JsonElement doc;
            try { doc = JsonSerializer.Deserialize<JsonElement>(existingContent); }
            catch { doc = default; }

            var ownerAgentInstanceId = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("agentInstanceId", out var agentProp)
                ? agentProp.GetString() : null;
            var ownerJobId = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("jobId", out var jobProp)
                ? jobProp.GetString() : null;
            var acquiredAt = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("acquiredAt", out var atProp)
                && DateTimeOffset.TryParse(atProp.GetString(), out var dt)
                ? dt : DateTimeOffset.MinValue;

            if (ownerAgentInstanceId is not null)
            {
                bool isActive;
                try
                {
                    isActive = _controlPlaneAgentClient is not null
                        && await _controlPlaneAgentClient.IsAgentActiveAsync(ownerAgentInstanceId, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex,
                        "[PackageLock] ControlPlane liveness check failed for {AgentId} — treating as stale.",
                        ownerAgentInstanceId);
                    isActive = false;
                }

                if (isActive)
                {
                    throw new PackageLockConflictException(
                        packagePath,
                        ownerJobId ?? string.Empty,
                        ownerAgentInstanceId,
                        acquiredAt);
                }

                _logger?.LogInformation(
                    "[PackageLock] Stale lock detected for agent {AgentId} (job {JobId}) — replacing.",
                    ownerAgentInstanceId, ownerJobId);
            }
        }

        try { File.Delete(lockFilePath); }
        catch { }

        using var fs = new FileStream(
            lockFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        var newLockContent = JsonSerializer.Serialize(new
        {
            jobId,
            agentInstanceId = _agentInstanceId.ToString(),
            acquiredAt = DateTimeOffset.UtcNow.ToString("O")
        });

        using var writer = new StreamWriter(fs);
        await writer.WriteAsync(newLockContent).ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        _logger?.LogInformation(
            "[PackageLock] Lock acquired (after stale removal) for job {JobId} by agent {AgentInstanceId} at {Path}",
            jobId, _agentInstanceId, lockFilePath);

        return new PackageLockHandle(lockFilePath, _logger);
    }

    private sealed class PackageLockHandle : IDisposable
    {
        private readonly string _lockFilePath;
        private readonly ILogger<ActivePackageAccess>? _logger;

        public PackageLockHandle(string lockFilePath, ILogger<ActivePackageAccess>? logger)
        {
            _lockFilePath = lockFilePath;
            _logger = logger;
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_lockFilePath))
                    File.Delete(_lockFilePath);
                else
                    _logger?.LogWarning(
                        "[PackageLock] Lock file {Path} was already missing on dispose — best-effort cleanup.",
                        _lockFilePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[PackageLock] Failed to delete lock file {Path} on dispose.",
                    _lockFilePath);
            }
        }
    }
}
