// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

public sealed record PackageContentContext(
    PackageContentKind Kind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    IPackageAddress? Address = null,
    bool IsCollectionRequest = false);