// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands;

/// <summary>
/// The child jobs one Migrate parent job is submitted as (ADR-0035): Export, then Prepare,
/// then Import, on the same package. Each child demands only the connector of the side it
/// touches (Export the source, Prepare and Import the target), and each depends on the one
/// before it, so the Control Plane can route the children to different agents in order.
/// This is routing only: the CLI runs nothing and writes nothing to the package.
/// </summary>
internal sealed record MigrateChildJobs(string ParentJobId, Job Export, Job Prepare, Job Import)
{
    internal static MigrateChildJobs Build(string rawJson, JobDiagnostics? diagnostics = null, JobResume? resume = null)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var mp = doc.RootElement.GetProperty("MigrationPlatform");
        var source = ConnectorsFor(ReadType(mp, "Source"));
        var target = ConnectorsFor(ReadType(mp, "Target"));

        var parentId = Guid.NewGuid().ToString();

        Job Child(JobKind kind, ConnectorType[] connectors, string? dependsOn) => new()
        {
            JobId = Guid.NewGuid().ToString(),
            Kind = kind,
            ConfigPayload = rawJson,
            Connectors = connectors,
            ParentJobId = parentId,
            DependsOnJobId = dependsOn,
            Diagnostics = diagnostics,
            Resume = resume
        };

        var export = Child(JobKind.Export, source, dependsOn: null);
        var prepare = Child(JobKind.Prepare, target, export.JobId);
        var import = Child(JobKind.Import, target, prepare.JobId);
        return new MigrateChildJobs(parentId, export, prepare, import);
    }

    /// <summary>
    /// Submits every child before any is followed, so the Control Plane knows the whole parent
    /// and a detached CLI leaves the migration running. Dependents go first and Export last: the
    /// Control Plane holds Import and Prepare until the job each depends on completes, and
    /// nothing runs until Export arrives. A submission that stops part-way therefore leaves a
    /// parent that is Queued, never one that reads Completed after Export alone.
    /// </summary>
    internal async Task SubmitAllAsync(IJobSubmissionClient client, CancellationToken cancellationToken)
    {
        await client.SubmitAsync(Import, cancellationToken).ConfigureAwait(false);
        await client.SubmitAsync(Prepare, cancellationToken).ConfigureAwait(false);
        await client.SubmitAsync(Export, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Maps a config connector type name to the connector an agent advertises.</summary>
    internal static ConnectorType? ConnectorFor(string? configType) => configType switch
    {
        _ when string.Equals(configType, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase) => ConnectorType.TeamFoundationServer,
        _ when string.Equals(configType, "AzureDevOpsServices", StringComparison.OrdinalIgnoreCase) => ConnectorType.AzureDevOps,
        _ when string.Equals(configType, "Simulated", StringComparison.OrdinalIgnoreCase) => ConnectorType.Simulated,
        _ => null
    };

    private static ConnectorType[] ConnectorsFor(string? configType) =>
        ConnectorFor(configType) is { } c ? [c] : [];

    private static string? ReadType(JsonElement mp, string side) =>
        mp.TryGetProperty(side, out var el)
        && el.ValueKind == JsonValueKind.Object
        && el.TryGetProperty("Type", out var type)
        && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;
}
