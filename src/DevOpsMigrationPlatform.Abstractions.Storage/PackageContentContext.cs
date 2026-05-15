// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

public sealed record PackageContentContext(
    PackageContentKind Kind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    IPackageContentAddress? Address = null,
    bool IsCollectionRequest = false);
