// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public interface IAnalyser
{
    string Name { get; }
    IReadOnlyList<ModuleDependency> DependsOn { get; }
    Task AnalyseAsync(AnalyseContext context, CancellationToken ct);
}

