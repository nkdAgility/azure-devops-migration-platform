using System;

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

    /// <summary>Log output folder: <c>.migration/Logs</c>.</summary>
    public const string Logs = $"{SystemRoot}/Logs";

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
    /// The phase-tracking file for Both-mode jobs:
    /// <c>.migration/Checkpoints/job.phase.json</c>.
    /// </summary>
    public const string PhaseFile = $"{Checkpoints}/job.phase.json";

    /// <summary>
    /// The execution plan file persisted after every task status transition:
    /// <c>.migration/Checkpoints/plan.json</c>.
    /// </summary>
    public const string PlanFile = $"{Checkpoints}/plan.json";

    /// <summary>
    /// Returns the OS-native filesystem path for the ID-map database,
    /// e.g. <c>&lt;packageRoot&gt;\.migration\Checkpoints\idmap.db</c>.
    /// Use this when constructing a SQLite connection string (which needs native separators).
    /// </summary>
    public static string IdMapDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, SystemRoot, "Checkpoints", "idmap.db");

    /// <summary>
    /// Returns a job-scoped log folder path,
    /// e.g. <c>.migration/Logs/638807123456789012-a1b2c3d4</c>.
    /// </summary>
    public static string JobLogFolder(long ticks, string jobId)
        => $"{Logs}/{ticks}-{jobId}";

    /// <summary>
    /// Returns the artefact-store key for an identity-mapping warning file,
    /// e.g. <c>.migration/Logs/identity-warnings/user%40example.com.log</c>.
    /// </summary>
    public static string IdentityWarning(string identity)
        => $"{Logs}/identity-warnings/{Uri.EscapeDataString(identity)}.log";

    // ── Legacy path helpers (pre-.migration packages) ────────────────────

    /// <summary>Legacy checkpoint prefix before the <c>.migration</c> layout was introduced.</summary>
    public const string LegacyCheckpoints = "Checkpoints";

    /// <summary>Legacy log prefix before the <c>.migration</c> layout was introduced.</summary>
    public const string LegacyLogs = "Logs";

    /// <summary>
    /// Returns the legacy cursor file path for backward-compatible reads,
    /// e.g. <c>Checkpoints/workitems.cursor.json</c>.
    /// </summary>
    public static string LegacyCursorFile(string moduleName)
        => $"{LegacyCheckpoints}/{moduleName.ToLowerInvariant()}.cursor.json";

    /// <summary>Legacy phase file: <c>Checkpoints/job.phase.json</c>.</summary>
    public const string LegacyPhaseFile = $"{LegacyCheckpoints}/job.phase.json";

    /// <summary>
    /// Returns the legacy OS-native path for the ID-map database.
    /// </summary>
    public static string LegacyIdMapDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, "Checkpoints", "idmap.db");

    /// <summary>
    /// Returns the OS-native filesystem path for the export-progress database,
    /// e.g. <c>&lt;packageRoot&gt;\.migration\Checkpoints\export_progress.db</c>.
    /// </summary>
    public static string ExportProgressDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, SystemRoot, "Checkpoints", "export_progress.db");

    /// <summary>Returns the legacy OS-native path for the export-progress database.</summary>
    public static string LegacyExportProgressDbNative(string packageLocalRoot)
        => System.IO.Path.Combine(packageLocalRoot, "Checkpoints", "export_progress.db");

    // ── Per-job configuration ─────────────────────────────────────────────

    /// <summary>
    /// Well-known path for the per-job migration configuration file at the package root.
    /// Written by the CLI before job submission; read by every agent at job start.
    /// Contains the full serialised <c>MigrationOptions</c> (source, target, credentials,
    /// modules, policies, and all tool options).
    /// </summary>
    public const string MigrationConfigFileName = "migration-config.json";
}
