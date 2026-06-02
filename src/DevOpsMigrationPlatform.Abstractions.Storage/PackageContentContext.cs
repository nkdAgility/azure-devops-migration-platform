// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

public sealed record PackageContentContext(
    PackageContentKind Kind,
    string Organisation,
    string Project,
    string Module,
    IPackageContentAddress? Address = null,
    bool IsCollectionRequest = false);
