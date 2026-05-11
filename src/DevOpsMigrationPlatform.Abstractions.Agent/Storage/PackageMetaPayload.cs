// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

public sealed record PackageMetaPayload(
    Stream Content,
    string? ContentType = null,
    string? ETag = null);

