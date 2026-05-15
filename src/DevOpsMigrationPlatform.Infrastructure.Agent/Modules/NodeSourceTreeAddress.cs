// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

internal sealed class NodeSourceTreeAddress : IPackageContentAddress
{
    public string RelativePath => "source-tree.json";
}
