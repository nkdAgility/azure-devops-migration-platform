// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// A single event emitted by a migration agent and batched to the control plane.
/// </summary>
public sealed record WorkerEvent(
    long Seq,
    DateTimeOffset Timestamp,
    WorkerEventKind Kind,
    string? PayloadJson);

public enum WorkerEventKind
{
    Heartbeat,
    Progress,
    Diagnostic,
    Metrics,
    Snapshot,
    Tasks,
    Terminal
}
