// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Single implementation of the <see cref="IProjectInventoryReader"/> and
/// <see cref="IProjectInventoryWriter"/> canonical ports (ADR-0023 / VS-H2).
/// Reads, merges, and writes the per-project <c>inventory.json</c> file behind the
/// package boundary; modules update only their own count field without clobbering
/// data written by another module.
/// </summary>
internal sealed class ProjectInventoryFileStore : IProjectInventoryReader, IProjectInventoryWriter
{
    private static readonly JsonSerializerOptions s_opts =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions s_writeOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc />
    public async Task MergeAsync(
        IPackageAccess package,
        string orgSlug,
        string projectName,
        string? orgUrl = null,
        long? workItems = null,
        long? revisions = null,
        int? repos = null,
        int? identities = null,
        int? nodes = null,
        int? teams = null,
        bool? isComplete = null,
        string? error = null,
        Dictionary<string, int>? areaPaths = null,
        CancellationToken ct = default)
    {
        var existing = await ReadAsync(package, orgSlug, projectName, ct).ConfigureAwait(false);

        var mergedAreaPaths = areaPaths ?? existing.AreaPathCounts;

        var updated = existing with
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            OrgUrl = orgUrl ?? existing.OrgUrl,
            Project = projectName,
            WorkItems = workItems ?? existing.WorkItems,
            Revisions = revisions ?? existing.Revisions,
            Repos = repos ?? existing.Repos,
            Identities = identities ?? existing.Identities,
            Nodes = nodes ?? existing.Nodes,
            Teams = teams ?? existing.Teams,
            IsComplete = isComplete ?? existing.IsComplete,
            Error = error ?? existing.Error,
            AreaPathCounts = mergedAreaPaths,
        };

        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated, s_writeOpts)), writable: false);
        await package.PersistIndexAsync(
            new PackageIndexContext("inventory.json", Organisation: orgSlug, Project: projectName),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ProjectInventoryData> ReadAsync(
        IPackageAccess package,
        string orgSlug,
        string projectName,
        CancellationToken ct)
    {
        var payload = await package.RequestIndexAsync(
            new PackageIndexContext("inventory.json", Organisation: orgSlug, Project: projectName),
            ct).ConfigureAwait(false);
        if (payload is null)
            return new ProjectInventoryData { GeneratedAt = DateTimeOffset.UtcNow };

        using var reader = new System.IO.StreamReader(payload.Content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<ProjectInventoryData>(json, s_opts)
                ?? new ProjectInventoryData { GeneratedAt = DateTimeOffset.UtcNow };
        }
        catch
        {
            return new ProjectInventoryData { GeneratedAt = DateTimeOffset.UtcNow };
        }
    }
}
