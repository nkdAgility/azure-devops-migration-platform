// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// A batch of <see cref="WorkerEvent"/> records posted by an agent to
/// <c>POST /workers/{workerId}/events</c>.
/// </summary>
public sealed record WorkerEventBatch(
    string WorkerId,
    string LeaseId,
    IReadOnlyList<WorkerEvent> Events);

/// <summary>
/// Acknowledgement returned by the control plane. The agent uses
/// <see cref="LastAcceptedSeq"/> to know which events were persisted.
/// </summary>
public sealed record WorkerEventAck(long LastAcceptedSeq);
