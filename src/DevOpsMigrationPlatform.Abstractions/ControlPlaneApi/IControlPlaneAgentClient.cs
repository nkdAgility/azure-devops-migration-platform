// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Abstraction over the control-plane HTTP endpoints used exclusively by the Migration Agent.
/// Declared in Abstractions so agent-side services
/// depend only on this narrow interface rather than the full <see cref="IControlPlaneClient"/>.
/// </summary>
/// <remarks>
/// <see cref="IControlPlaneClient"/> covers CLI/TUI operations (job listing, log streaming,
/// telemetry polling). This interface covers agent-process operations. Separating them
/// satisfies ISP — the agent never needs list/stream functionality, the CLI never needs
/// stale-lock detection.
/// </remarks>
public interface IControlPlaneAgentClient
{
    /// <summary>
    /// Checks whether the agent with <paramref name="agentInstanceId"/> is currently active.
    /// Used by agent coordination flows to distinguish live locks from stale locks.
    /// Returns <see langword="false"/> if the status cannot be determined (e.g. network error).
    /// </summary>
    Task<bool> IsAgentActiveAsync(string agentInstanceId, CancellationToken ct);
}
