// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// A within-module relative path address.
/// Valid only when <see cref="PackageContentContext.Module"/>,
/// <see cref="PackageContentContext.Organisation"/>, and
/// <see cref="PackageContentContext.Project"/> are all set on the context.
/// The path must not include the module, project, or organisation segment —
/// it is relative to the module folder root.
/// </summary>
public sealed class RelativePathAddress : IPackageContentAddress
{
    public RelativePathAddress(string relativePath)
    {
        if (relativePath is null)
            throw new ArgumentNullException(nameof(relativePath));

        RelativePath = relativePath.Replace('\\', '/').TrimStart('/');
    }

    public string RelativePath { get; }
}
