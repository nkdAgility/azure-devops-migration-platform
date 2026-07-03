// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;

/// <summary>
/// Canonical port for the unified agent-to-control-plane worker event channel (ADR-0020,
/// ADR-0023 / CA-C1). Job workers depend on this port; the concrete
/// <c>UnifiedWorkerEventWriter</c> in Infrastructure.Agent implements it.
/// </summary>
public interface IWorkerEventWriter
{
    /// <summary>Enqueues the job task list (execution plan) for display on the control plane.</summary>
    void EnqueueTasks(JobTaskList tasks);

    /// <summary>
    /// Enqueues the terminal signal for the current job. Terminal events bypass the
    /// batch timer and are flushed immediately.
    /// </summary>
    void EnqueueTerminal(bool failed);

    /// <summary>Drains and posts any buffered events before the process is torn down.</summary>
    Task FlushAsync();
}
