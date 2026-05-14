// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Result of a meta read operation. Provides the resolved physical path for diagnostics/logging
/// and the payload (null when the meta file does not exist).
/// </summary>
public sealed record PackageMetaResult(
    string ResolvedPath,
    PackageMetaPayload? Payload);
