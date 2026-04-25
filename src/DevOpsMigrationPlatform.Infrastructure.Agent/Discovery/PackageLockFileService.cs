#if !NET481
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// File-system-backed implementation of <see cref="IPackageLockService"/>.
/// Uses <see cref="FileMode.CreateNew"/> for atomic lock acquisition.
/// </summary>
/// <remarks>
/// <b>Architectural exception — direct filesystem I/O</b>:
/// Lock acquisition requires an atomic OS-level operation (<see cref="FileMode.CreateNew"/>) that
/// cannot be implemented through <see cref="IArtefactStore"/>. This class is infrastructure-layer
/// code only; all module and domain code must continue to access package content exclusively
/// through <see cref="IArtefactStore"/>. See guardrails rule 13 and the package-lock contract doc.
/// </remarks>
public sealed class PackageLockFileService : IPackageLockService
{
    private readonly Guid _agentInstanceId;
    private readonly IControlPlaneClient _controlPlane;
    private readonly ILogger<PackageLockFileService> _logger;

    public PackageLockFileService(
        Guid agentInstanceId,
        IControlPlaneClient controlPlane,
        ILogger<PackageLockFileService> logger)
    {
        _agentInstanceId = agentInstanceId;
        _controlPlane = controlPlane ?? throw new ArgumentNullException(nameof(controlPlane));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<IAsyncDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct)
    {
        var localRoot = ResolveLocalPath(packagePath);
        var lockFilePath = Path.Combine(localRoot, PackagePaths.SystemRoot, "Checkpoints", "agent.lock");
        var dir = Path.GetDirectoryName(lockFilePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        return await TryAcquireAsync(lockFilePath, localRoot, jobId, ct).ConfigureAwait(false);
    }

    private async Task<IAsyncDisposable> TryAcquireAsync(
        string lockFilePath, string packagePath, string jobId, CancellationToken ct)
    {
        try
        {
            await using var fs = new FileStream(
                lockFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            var lockContent = JsonSerializer.Serialize(new
            {
                jobId,
                agentInstanceId = _agentInstanceId.ToString(),
                acquiredAt = DateTimeOffset.UtcNow.ToString("O")
            });

            await using var writer = new StreamWriter(fs);
            await writer.WriteAsync(lockContent).ConfigureAwait(false);
            await writer.FlushAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "[PackageLock] Lock acquired for job {JobId} by agent {AgentInstanceId} at {Path}",
                jobId, _agentInstanceId, lockFilePath);

            return new PackageLockHandle(lockFilePath, _logger);
        }
        catch (IOException)
        {
            // File exists — read existing lock and check liveness
            return await HandleExistingLockAsync(lockFilePath, packagePath, jobId, ct).ConfigureAwait(false);
        }
    }

    private async Task<IAsyncDisposable> HandleExistingLockAsync(
        string lockFilePath, string packagePath, string jobId, CancellationToken ct)
    {
        string? existingContent = null;
        try
        {
            existingContent = await File.ReadAllTextAsync(lockFilePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[PackageLock] Could not read existing lock file at {Path} — treating as stale.",
                lockFilePath);
        }

        if (existingContent is not null)
        {
            JsonElement doc;
            try { doc = JsonSerializer.Deserialize<JsonElement>(existingContent); }
            catch { doc = default; }

            var ownerAgentInstanceId = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("agentInstanceId", out var agentProp)
                ? agentProp.GetString() : null;
            var ownerJobId = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("jobId", out var jobProp)
                ? jobProp.GetString() : null;
            var acquiredAt = doc.ValueKind != JsonValueKind.Undefined
                && doc.TryGetProperty("acquiredAt", out var atProp)
                && DateTimeOffset.TryParse(atProp.GetString(), out var dt)
                ? dt : DateTimeOffset.MinValue;

            if (ownerAgentInstanceId is not null)
            {
                bool isActive;
                try
                {
                    isActive = await _controlPlane
                        .IsAgentActiveAsync(ownerAgentInstanceId, ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[PackageLock] ControlPlane liveness check failed for {AgentId} — treating as stale.",
                        ownerAgentInstanceId);
                    isActive = false;
                }

                if (isActive)
                {
                    throw new PackageLockConflictException(
                        packagePath,
                        ownerJobId ?? string.Empty,
                        ownerAgentInstanceId,
                        acquiredAt);
                }

                _logger.LogInformation(
                    "[PackageLock] Stale lock detected for agent {AgentId} (job {JobId}) — replacing.",
                    ownerAgentInstanceId, ownerJobId);
            }
        }

        // Stale lock — delete and retry once
        try { File.Delete(lockFilePath); }
        catch { /* best effort */ }

        // Retry once after deleting stale lock
        await using var fs = new FileStream(
            lockFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        var newLockContent = JsonSerializer.Serialize(new
        {
            jobId,
            agentInstanceId = _agentInstanceId.ToString(),
            acquiredAt = DateTimeOffset.UtcNow.ToString("O")
        });

        await using var writer = new StreamWriter(fs);
        await writer.WriteAsync(newLockContent).ConfigureAwait(false);
        await writer.FlushAsync(ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[PackageLock] Lock acquired (after stale removal) for job {JobId} by agent {AgentInstanceId} at {Path}",
            jobId, _agentInstanceId, lockFilePath);

        return new PackageLockHandle(lockFilePath, _logger);
    }

    private static string ResolveLocalPath(string packagePath)
    {
        if (packagePath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return packagePath["file:///".Length..].Replace('/', Path.DirectorySeparatorChar);
        return packagePath;
    }

    private sealed class PackageLockHandle : IAsyncDisposable
    {
        private readonly string _lockFilePath;
        private readonly ILogger _logger;

        public PackageLockHandle(string lockFilePath, ILogger logger)
        {
            _lockFilePath = lockFilePath;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (File.Exists(_lockFilePath))
                    File.Delete(_lockFilePath);
                else
                    _logger.LogWarning(
                        "[PackageLock] Lock file {Path} was already missing on dispose — best-effort cleanup.",
                        _lockFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[PackageLock] Failed to delete lock file {Path} on dispose.",
                    _lockFilePath);
            }
            return ValueTask.CompletedTask;
        }
    }
}
#endif
