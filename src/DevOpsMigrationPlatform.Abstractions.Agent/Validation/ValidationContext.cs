// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Storage;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Validation;

/// <summary>
/// Context passed to IModule.ValidateAsync.
/// Validation is side-effect free — no writes to the package or target are permitted.
/// </summary>
public class ValidationContext
{
    public Job Job { get; init; } = null!;
    public IPackageAccess Package { get; init; } = null!;
    public System.Collections.Generic.List<ValidationError> Errors { get; } = new();
}
