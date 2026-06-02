// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Addresses a package-level index/summary file (e.g. inventory) that lives at a scope
/// root rather than inside a module. The caller supplies the filename it already knows;
/// the router owns only the scope prefix.
///
/// Scope is implied by which fields are set:
///   neither      → {FileName}                         (package root)
///   org          → {Organisation}/{FileName}
///   org+project  → {Organisation}/{Project}/{FileName}
///
/// <see cref="FileName"/> MUST be a bare filename — never a path. It may not contain
/// '/', '\\', or '..'. This is what prevents it becoming a free-path crutch: an index
/// file can never escape its scope root or reach into a module subtree.
/// </summary>
public sealed record PackageIndexContext(
    string FileName,
    string? Organisation = null,
    string? Project = null);
