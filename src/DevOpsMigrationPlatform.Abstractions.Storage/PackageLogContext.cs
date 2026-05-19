// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

public sealed record PackageLogContext(
    string RunId,
    PackageLogStream Stream,
    bool AllowRotation = true);

