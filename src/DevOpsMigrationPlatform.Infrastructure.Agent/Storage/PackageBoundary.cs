// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

internal sealed class PackageBoundary : IPackage
{
    private readonly ActivePackageState _activePackageState;
    private readonly PackagePathRouter _router;
    private readonly ILogger<PackageBoundary> _logger;

    public PackageBoundary(
        ActivePackageState activePackageState,
        PackagePathRouter router,
        ILogger<PackageBoundary> logger)
    {
        _activePackageState = activePackageState ?? throw new ArgumentNullException(nameof(activePackageState));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<PackagePayload?> RequestAsync(
        PackageContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        var content = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return null;

        return new PackagePayload(
            new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false),
            "application/json");
    }

    public async ValueTask<PackageMetaPayload?> RequestMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveMetaPath(context);
        var content = await store.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (content is null)
            return null;

        return new PackageMetaPayload(
            new MemoryStream(Encoding.UTF8.GetBytes(content), writable: false),
            "application/json");
    }

    public async ValueTask PersistAsync(
        PackageContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveContentPath(context);
        var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
        await store.WriteAsync(path, content, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Persisted package content to {Path}", path);
    }

    public async ValueTask PersistMetaAsync(
        PackageMetaContext context,
        PackageMetaPayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
        var authoritativePath = _router.ResolveMetaPath(context);
        await store.WriteAsync(authoritativePath, content, cancellationToken).ConfigureAwait(false);

        if (context.RelatedToRun && !string.IsNullOrWhiteSpace(_activePackageState.CurrentRunId))
        {
            var runAuditPath = _router.ResolveMetaPath(context, _activePackageState.CurrentRunId, runAudit: true);
            await store.WriteAsync(runAuditPath, content, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask AppendLogAsync(
        PackageLogContext context,
        PackageLogPayload payload,
        CancellationToken cancellationToken = default)
    {
        var store = RequireStore();
        var path = _router.ResolveLogPath(context);
        var content = await ReadUtf8Async(payload.Content, cancellationToken).ConfigureAwait(false);
        await store.AppendAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    private IArtefactStore RequireStore()
        => _activePackageState.CurrentStore
            ?? throw new InvalidOperationException("No active package store is available.");

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
}

