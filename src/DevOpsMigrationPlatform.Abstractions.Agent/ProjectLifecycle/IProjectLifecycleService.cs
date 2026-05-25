// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

/// <summary>
/// Connector-facing lifecycle contract for creating and tearing down ephemeral
/// projects used by test runs.
/// </summary>
public interface IProjectLifecycleService
{
    /// <summary>
    /// Creates an ephemeral project for the supplied run.
    /// </summary>
    Task<ProjectLifecycleRecord> CreateAsync(
        ProjectLifecycleContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tears down the project represented by <paramref name="record"/>.
    /// </summary>
    Task<ProjectLifecycleRecord> TeardownAsync(
        ProjectLifecycleRecord record,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context used for lifecycle create orchestration.
/// </summary>
public sealed class ProjectLifecycleContext
{
    public string RunId { get; init; } = string.Empty;
    public string ConnectorType { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string? NamePrefix { get; init; }
    public string? ProcessName { get; init; }
    public OrganisationEndpoint Endpoint { get; init; } = new();
}
