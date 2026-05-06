// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Globalization;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Lease;

/// <summary>
/// Centralised well-known paths for system files inside a migration package.
/// All checkpoint, log, and identity-map paths are derived from this class
/// so the prefix can be changed in a single place.
/// </summary>
/// <remarks>
/// Paths use forward-slash separators — <see cref="IArtefactStore"/> and
/// <see cref="IStateStore"/> normalise to the platform separator internally.
/// </remarks>
public static class PackagePaths
{
    /// <summary>
    /// Root folder for all system/operational files inside a package.
    /// Hidden by convention (dot-prefix) so user data folders remain uncluttered.
    /// </summary>
    public const string SystemRoot = ".migration";

    /// <summary>Checkpoint cursor folder: <c>.migration/Checkpoints</c>.</summary>
    public const string Checkpoints = $"{SystemRoot}/Checkpoints";

    /// <summary>
    /// Fallback log folder used when no job is active: <c>.migration/Logs</c>.
    /// Under normal operation all log writes go to <see cref="RunLogsFolder"/>.
    /// </summary>
    public const string Logs = $"{SystemRoot}/Logs";

    /// <summary>
    /// Root folder for per-run subfolders: <c>.migration/runs</c>.
    /// Each run creates a child folder named <c>yyyyMMdd-HHmmss</c> containing
    /// <c>logs/</c> and <c>audit/</c> subdirectories.
    /// </summary>
    public const string RunsRoot = $"{SystemRoot}/runs";

    /// <summary>
    /// Builds the canonical run identifier string from the run start time and job:
    /// <c>yyyyMMdd-HHmmss</c>, e.g. <c>20260506-143822</c>.
    /// The <paramref name="job"/> parameter keeps the signature future-proof for
    /// scenarios where additional job metadata influences the run ID.
    /// </summary>
    public static string BuildRunId(DateTimeOffset startedAtUtc, Job job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        return startedAtUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Returns the root folder for a specific run,
    /// e.g. <c>.migration/runs/20260506-143822</c>.
    /// </summary>
    /// <param name="runId">The run identifier — use <see cref="BuildRunId"/> to construct.</param>
    public static string RunFolder(string runId)
        => $"{RunsRoot}/{runId}";

    /// <summary>
    /// Returns the log folder for a run,
    /// e.g. <c>.migration/runs/20260506-143822/logs</c>.
    /// </summary>
    public static string RunLogsFolder(string runId)
        => $"{RunFolder(runId)}/logs";

    /// <summary>
    /// Returns the audit folder for a run,
    /// e.g. <c>.migration/runs/20260506-143822/audit</c>.
    /// </summary>
    public static string RunAuditFolder(string runId)
        => $"{RunFolder(runId)}/audit";

    /// <summary>
    /// Returns the audit plan file path for a run,
    /// e.g. <c>.migration/runs/20260506-143822/audit/migration-plan.json</c>.
    /// </summary>
    public static string RunAuditPlanFile(string runId)
        => $"{RunAuditFolder(runId)}/migration-plan.json";

    /// <summary>
    /// Returns the audit config file path for a run,
    /// e.g. <c>.migration/runs/20260506-143822/audit/migration-config.json</c>.
    /// </summary>
    public static string RunAuditConfigFile(string runId)
        => $"{RunAuditFolder(runId)}/migration-config.json";

    /// <summary>
    /// Returns the run-level job metadata path,
    /// e.g. <c>.migration/runs/20260506-143822/job.json</c>.
    /// </summary>
    public static string RunJobFile(string runId)
        => $"{RunFolder(runId)}/job.json";

    /// <summary>
    /// Returns the artefact-store key for a module's cursor file,
    /// e.g. <c>.migration/Checkpoints/workitems.cursor.json</c>.
    /// </summary>
    public static string CursorFile(string moduleName)
        => $"{Checkpoints}/{moduleName.ToLowerInvariant()}.cursor.json";

    /// <summary>
    /// Returns the artefact-store key for a module's continuation token file,
    /// e.g. <c>.migration/Checkpoints/inventory.continuation.json</c>.
    /// Scoped per-module to prevent concurrent callers from corrupting each other.
    /// </summary>
    public static string ContinuationFile(string moduleName)
        => $"{Checkpoints}/{moduleName.ToLowerInvariant()}.continuation.json";

    /// <summary>
    /// The phase-tracking file for Migrate-mode jobs:
    /// <c>.migration/Checkpoints/job.phase.json</c>.
    /// </summary>
    public const string PhaseFile = $"{Checkpoints}/job.phase.json";

    /// <summary>
    /// The execution plan file persisted after every task status transition:
    /// <c>.migration/plan.json</c>.
    /// </summary>
    public const string PlanFile = $"{SystemRoot}/plan.json";

    /// <summary>
    /// Returns the OS-native filesystem path for the ID-map database,
    /// e.g. <c>&lt;packageRoot&gt;\.migration\Checkpoints\idmap.db</c>.
    /// Use this when constructing a SQLite connection string (which needs native separators).
    /// </summary>
    public static string IdMapDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, SystemRoot, "Checkpoints", "idmap.db");

    /// <summary>
    /// Returns the artefact-store key for an identity-mapping warning file,
    /// e.g. <c>.migration/identity-warnings/user%40example.com.log</c>.
    /// These are package-level (not run-scoped) so they accumulate across runs.
    /// </summary>
    public static string IdentityWarning(string identity)
        => $"{SystemRoot}/identity-warnings/{Uri.EscapeDataString(identity)}.log";

    /// <summary>
    /// Returns the OS-native filesystem path for the export-progress database,
    /// e.g. <c>&lt;packageRoot&gt;\.migration\Checkpoints\export_progress.db</c>.
    /// </summary>
    public static string ExportProgressDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, SystemRoot, "Checkpoints", "export_progress.db");

    // ── Per-job configuration ─────────────────────────────────────────────

    /// <summary>
    /// Well-known path for the per-job migration configuration file inside <c>.migration/</c>.
    /// Written by the CLI before job submission; read by every agent at job start.
    /// Contains the full serialised <c>MigrationPlatformOptions</c> (source, target, credentials,
    /// modules, policies, and all tool options).
    /// </summary>
    public const string MigrationConfigFileName = $"{SystemRoot}/migration-config.json";

}
