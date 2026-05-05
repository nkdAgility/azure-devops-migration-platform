// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Analysis;

public sealed record EndpointPairAnalyseContext : AnalyseContext
{
    public required ISourceEndpointInfo SourceEndpoint { get; init; }
    public required ITargetEndpointInfo TargetEndpoint { get; init; }
}

