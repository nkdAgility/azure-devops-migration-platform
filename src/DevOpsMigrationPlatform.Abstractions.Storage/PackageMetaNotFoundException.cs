// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Storage-neutral exception thrown by <see cref="IPackageAccess.ResetMetaAsync"/>
/// implementations when the addressed meta artefact does not exist and the backing
/// store cannot treat the reset as an idempotent no-op. Mirrors the
/// <see cref="PackageConfigNotFoundException"/> pattern so that package-boundary
/// consumers never depend on filesystem exception types (ADR-0025, HX-H1).
/// </summary>
public sealed class PackageMetaNotFoundException : Exception
{
    /// <summary>The meta kind that was addressed.</summary>
    public PackageMetaKind Kind { get; }

    /// <summary>
    /// Initialises a new instance for the meta kind that could not be found.
    /// </summary>
    public PackageMetaNotFoundException(PackageMetaKind kind)
        : base($"Package meta artefact '{kind}' not found in the active package.")
    {
        Kind = kind;
    }

    /// <summary>
    /// Initialises a new instance with the storage-specific inner exception that
    /// the adapter translated.
    /// </summary>
    public PackageMetaNotFoundException(PackageMetaKind kind, Exception innerException)
        : base($"Package meta artefact '{kind}' not found in the active package.", innerException)
    {
        Kind = kind;
    }
}
