// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Aggregate counters for a running job, pushed by the agent on a fast timer.
/// Maps to the OTel Metrics analogy — what is the current count.
/// <para>
/// <see cref="Scope"/> is always populated. Exactly one of <see cref="Migration"/>
/// or <see cref="Discovery"/> is non-null depending on job type.
/// </para>
/// <para>
/// Cardinality guardrail: <c>JobMetrics</c> is aggregate-only — it carries no per-entity,
/// per-project, or per-work-item dimensions. All high-cardinality breakdowns belong
/// exclusively in <see cref="JobSnapshot"/>.
/// </para>
/// </summary>
public record JobMetrics
{
    /// <summary>UTC timestamp when this metrics record was produced.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Scope-level counters shared by all job types.</summary>
    public JobScopeCounters Scope { get; init; } = new();

    /// <summary>Migration-specific counters. Null for discovery jobs.</summary>
    public MigrationCounters? Migration { get; init; }

    /// <summary>Discovery-specific counters. Null for migration jobs.</summary>
    public DiscoveryCounters? Discovery { get; init; }
}
