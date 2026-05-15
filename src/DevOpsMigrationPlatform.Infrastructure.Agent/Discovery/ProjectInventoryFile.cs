// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Strongly-typed representation of the per-project inventory file written to
/// <c>{orgSlug}/{project}/inventory.json</c>. All module inventory counts are
/// merged into this single file; no per-module inventory files are written.
/// </summary>
internal sealed record ProjectInventoryData
{
    public DateTimeOffset GeneratedAt { get; init; }
    public string OrgUrl { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public long WorkItems { get; init; }
    public long Revisions { get; init; }
    public int Repos { get; init; }
    public int Identities { get; init; }
    public int Nodes { get; init; }
    public int Teams { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Read/merge/write helpers for per-project inventory files.
/// Modules call <see cref="MergeAsync"/> to update only their own count field
/// without clobbering data written by another module.
/// </summary>
internal static class ProjectInventoryFile
{
    private static readonly JsonSerializerOptions s_opts =
        new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonSerializerOptions s_writeOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Reads the existing per-project inventory file (or returns an empty record),
    /// applies the supplied delta fields, and writes the result back.
    /// </summary>
    public static async Task MergeAsync(
        IPackageAccess package,
        string path,
        string? orgUrl = null,
        string? project = null,
        long? workItems = null,
        long? revisions = null,
        int? repos = null,
        int? identities = null,
        int? nodes = null,
        int? teams = null,
        bool? isComplete = null,
        string? error = null,
        CancellationToken ct = default)
    {
        var existing = await ReadAsync(package, path, ct).ConfigureAwait(false);

        var updated = existing with
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            OrgUrl = orgUrl ?? existing.OrgUrl,
            Project = project ?? existing.Project,
            WorkItems = workItems ?? existing.WorkItems,
            Revisions = revisions ?? existing.Revisions,
            Repos = repos ?? existing.Repos,
            Identities = identities ?? existing.Identities,
            Nodes = nodes ?? existing.Nodes,
            Teams = teams ?? existing.Teams,
            IsComplete = isComplete ?? existing.IsComplete,
            Error = error ?? existing.Error,
        };

        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated, s_writeOpts)), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(path)),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);
    }

    public static async Task<ProjectInventoryData> ReadAsync(
        IPackageAccess package,
        string path,
        CancellationToken ct)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(path)),
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

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
