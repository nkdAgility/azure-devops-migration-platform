// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

internal sealed class ProjectInventoryCsvAddress : IPackageContentAddress
{
    public string RelativePath => "inventory.csv";
}

internal sealed class AggregateInventoryCsvAddress : IPackageContentAddress
{
    public string RelativePath => "inventory.csv";
}

internal sealed class AggregateInventoryJsonAddress : IPackageContentAddress
{
    public string RelativePath => "aggregate.json";
}
