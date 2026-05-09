// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

public sealed record PackageContext(
    string ContentKind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    string? Scope = null,
    string? ItemKey = null,
    bool IsCollectionRequest = false);

