// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>How the Control Plane decides a queued job cannot be served (#169, ADR-0035).</summary>
public sealed class JobRoutingOptions
{
    public const string SectionName = "JobRouting";

    /// <summary>
    /// How long a leasable job may wait while no agent able to serve its connectors asks for
    /// work before the job is failed. Must be longer than an agent's 30-second lease poll.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan UnservableJobTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a leased job's agent may send no heartbeat or worker event before the job is
    /// failed as abandoned (#171). Agents heartbeat every 15 seconds, so the default allows eight
    /// missed heartbeats.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan SilentLeaseTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How often the Control Plane checks for unservable jobs and silent leases.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "00:10:00")]
    public TimeSpan CheckInterval { get; init; } = TimeSpan.FromSeconds(15);
}
