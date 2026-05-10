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
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

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

    public ActivePackageAccess(
        ActivePackageState activePackageState,
        PackagePathRouter router)
    {
        _activePackageState = activePackageState ?? throw new ArgumentNullException(nameof(activePackageState));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    public ActivePackageAccess(
        ActivePackageState activePackageState,
        PackagePathRouter router,
        ILogger<ActivePackageAccess> logger)
        : this(activePackageState, router)
    {
        _ = logger;
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

    public async ValueTask<PackageMetaPayload?> RequestMetaAsync(
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
                if (content is null)
                    return null;

                return new PackageMetaPayload(
                    new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false),
                    "application/json");
            },
            context.Kind.ToString()).ConfigureAwait(false);
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
}

