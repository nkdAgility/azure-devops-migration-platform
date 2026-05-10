// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Caller-facing package boundary that routes typed package intents to persistence stores.
/// </summary>
public interface IPackage
{
    ValueTask<PackagePayload?> RequestAsync(
        PackageContext context,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ExistsAsync(
        PackageContext context,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> EnumerateAsync(
        PackageContext context,
        CancellationToken cancellationToken = default);

    ValueTask<PackagePayload?> RequestBinaryAsync(
        PackageContext context,
        CancellationToken cancellationToken = default);

    ValueTask<PackageMetaPayload?> RequestMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default);

    ValueTask PersistAsync(
        PackageContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default);

    ValueTask PersistStreamAsync(
        PackageContext context,
        Stream payload,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    ValueTask PersistMetaAsync(
        PackageMetaContext context,
        PackageMetaPayload payload,
        CancellationToken cancellationToken = default);

    ValueTask AppendAsync(
        PackageContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default);

    ValueTask AppendLogAsync(
        PackageLogContext context,
        PackageLogPayload payload,
        CancellationToken cancellationToken = default);
}

