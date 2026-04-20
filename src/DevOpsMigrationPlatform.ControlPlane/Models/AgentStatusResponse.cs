namespace DevOpsMigrationPlatform.ControlPlane.Models;

/// <summary>
/// Response body for <c>GET /agents/{agentInstanceId}/status</c>.
/// </summary>
/// <param name="Status">
/// <c>"Active"</c> when the agent has polled recently; additional values may be added in future.
/// </param>
public sealed record AgentStatusResponse(string Status);
