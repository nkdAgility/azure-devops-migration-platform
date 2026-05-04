// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public interface IEndpointPairAnalyser : IAnalyser
{
    Task AnalyseAsync(EndpointPairAnalyseContext context, CancellationToken ct);
}

