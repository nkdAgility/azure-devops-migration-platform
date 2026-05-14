// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Lease;

internal static class PackagePathTestHelper
{
    public const string SystemRoot = ".migration";
    public const string Logs = $"{SystemRoot}/Logs";
    public const string PlanFile = $"{SystemRoot}/plan.json";
    public const string PhaseFile = $"{SystemRoot}/phase.json";
    public const string MigrationConfigFileName = $"{SystemRoot}/migration-config.json";
    public const string InventoryCompleteFile = $"{SystemRoot}/inventory.complete.json";

    public static string RunPlanFile(string runId)
        => $"{SystemRoot}/runs/{runId}/audit/migration-plan.json";

    public static string CursorFile(string moduleName)
        => $"{SystemRoot}/Checkpoints/{moduleName.ToLowerInvariant()}.cursor.json";

    public static string CursorFile(string action, string moduleName, string organisationUrl, string projectName)
        => $"{SystemRoot}/{action.ToLowerInvariant()}.{moduleName.ToLowerInvariant()}.cursor.json";

    public static string ContinuationFile(string moduleName)
        => $"{SystemRoot}/Checkpoints/{moduleName.ToLowerInvariant()}.continuation.json";

    public static string ContinuationFile(string action, string moduleName, string organisationUrl, string projectName)
        => $"{SystemRoot}/{action.ToLowerInvariant()}.{moduleName.ToLowerInvariant()}.continuation.json";
}
