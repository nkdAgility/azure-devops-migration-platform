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
public interface IPackageAccess
{
	ValueTask<PackagePayload?> RequestContentAsync(
		PackageContentContext context,
		CancellationToken cancellationToken = default);

	ValueTask<bool> ContentExistsAsync(
		PackageContentContext context,
		CancellationToken cancellationToken = default);

	IAsyncEnumerable<string> EnumerateContentAsync(
		PackageContentContext context,
		CancellationToken cancellationToken = default);

	ValueTask<Stream?> RequestContentBinaryAsync(
		PackageContentContext context,
		CancellationToken cancellationToken = default);

	ValueTask<PackageMetaPayload?> RequestMetaAsync(
		PackageMetaContext context,
		CancellationToken cancellationToken = default);

	ValueTask PersistContentAsync(
		PackageContentContext context,
		PackagePayload payload,
		CancellationToken cancellationToken = default);

	ValueTask PersistContentStreamAsync(
		PackageContentContext context,
		Stream payload,
		string? contentType = null,
		CancellationToken cancellationToken = default);

	ValueTask PersistMetaAsync(
		PackageMetaContext context,
		PackageMetaPayload payload,
		CancellationToken cancellationToken = default);

	ValueTask AppendContentAsync(
		PackageContentContext context,
		PackagePayload payload,
		CancellationToken cancellationToken = default);

	ValueTask AppendLogAsync(
		PackageLogContext context,
		PackageLogPayload payload,
		CancellationToken cancellationToken = default);
}

