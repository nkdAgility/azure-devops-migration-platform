// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public sealed record EndpointPairAnalyseContext : AnalyseContext
{
    public ISourceEndpointInfo SourceEndpoint { get; init; } = null!;
    public ITargetEndpointInfo TargetEndpoint { get; init; } = null!;
}

