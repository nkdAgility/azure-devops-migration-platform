using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Models;

/// <summary>Response body returned when an agent successfully acquires a job lease.</summary>
public sealed record AgentLeaseResponse(string LeaseId, MigrationJob Job);
