// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Context passed to IModule.ImportAsync.
/// Import reads from and writes to the package via IPackageAccess.
/// </summary>
public class ImportContext
{
    public Job Job { get; init; } = null!;
    public IPackageAccess Package { get; init; } = null!;
    public IProgressSink ProgressSink { get; init; } = null!;
}

