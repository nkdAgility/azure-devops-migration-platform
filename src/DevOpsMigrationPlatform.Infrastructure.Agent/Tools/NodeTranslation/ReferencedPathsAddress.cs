// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

internal sealed class ReferencedPathsAddress : IPackageContentAddress
{
    public string RelativePath => "referenced-paths.json";
}
