// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

internal sealed class InMemoryPackageAccess : IPackageAccess, IDisposable
{
    private readonly string _root;
    private readonly FileSystemArtefactStore _inner;
    private readonly ActivePackageAccess _package;

    public InMemoryPackageAccess()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _inner = new FileSystemArtefactStore(_root);

        var state = new ActivePackageState
        {
            CurrentJob = new Job
            {
                JobId = "test-job",
                Kind = JobKind.Export,
                Package = new JobPackage { PackageUri = _root }
            }
        };

        _package = new ActivePackageAccess(state, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);
    }

    public string Root => _root;

    public static implicit operator FileSystemArtefactStore(InMemoryPackageAccess store) => store._inner;

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
        => _inner.ReadAsync(path, cancellationToken);

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        => _inner.WriteAsync(path, content, cancellationToken);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => _inner.ExistsAsync(path, cancellationToken);

    public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
        => _inner.WriteBinaryAsync(path, content, cancellationToken);

    public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
        => _inner.ReadBinaryAsync(path, cancellationToken);

    public IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken)
        => _inner.EnumerateAsync(prefix, cancellationToken);

    public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
        => _inner.WriteStreamAsync(path, content, cancellationToken);

    public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        => _inner.AppendAsync(path, content, cancellationToken);

    public ValueTask<PackagePayload?> RequestContentAsync(PackageContentContext context, CancellationToken cancellationToken = default)
        => _package.RequestContentAsync(context, cancellationToken);

    public ValueTask<bool> ContentExistsAsync(PackageContentContext context, CancellationToken cancellationToken = default)
        => _package.ContentExistsAsync(context, cancellationToken);

    public IAsyncEnumerable<string> EnumerateContentAsync(PackageContentContext context, CancellationToken cancellationToken = default)
        => _package.EnumerateContentAsync(context, cancellationToken);

    public ValueTask<Stream?> RequestContentBinaryAsync(PackageContentContext context, CancellationToken cancellationToken = default)
        => _package.RequestContentBinaryAsync(context, cancellationToken);

    public ValueTask<PackageMetaResult> RequestMetaAsync(PackageMetaContext context, CancellationToken cancellationToken = default)
        => _package.RequestMetaAsync(context, cancellationToken);

    public ValueTask<DbConnection> OpenNativeDatabaseAsync(PackageMetaKind kind, CancellationToken cancellationToken = default)
        => _package.OpenNativeDatabaseAsync(kind, cancellationToken);

    public ValueTask<IDisposable> AcquireLockAsync(string jobId, CancellationToken cancellationToken = default)
        => _package.AcquireLockAsync(jobId, cancellationToken);

    public ValueTask PersistContentAsync(PackageContentContext context, PackagePayload payload, CancellationToken cancellationToken = default)
        => _package.PersistContentAsync(context, payload, cancellationToken);

    public ValueTask PersistContentStreamAsync(PackageContentContext context, Stream payload, string? contentType = null, CancellationToken cancellationToken = default)
        => _package.PersistContentStreamAsync(context, payload, contentType, cancellationToken);

    public ValueTask PersistMetaAsync(PackageMetaContext context, PackageMetaPayload payload, CancellationToken cancellationToken = default)
        => _package.PersistMetaAsync(context, payload, cancellationToken);

    public ValueTask ResetMetaAsync(PackageMetaContext context, CancellationToken cancellationToken = default)
        => _package.ResetMetaAsync(context, cancellationToken);

    public ValueTask AppendContentAsync(PackageContentContext context, PackagePayload payload, CancellationToken cancellationToken = default)
        => _package.AppendContentAsync(context, payload, cancellationToken);

    public ValueTask AppendLogAsync(PackageLogContext context, PackageLogPayload payload, CancellationToken cancellationToken = default)
        => _package.AppendLogAsync(context, payload, cancellationToken);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
