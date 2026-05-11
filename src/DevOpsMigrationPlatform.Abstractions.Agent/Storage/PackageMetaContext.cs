// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

public sealed record PackageMetaContext(
    PackageMetaKind Kind,
    string? Organisation = null,
    string? Project = null,
    bool RelatedToRun = false);

