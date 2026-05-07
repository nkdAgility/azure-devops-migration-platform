// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.State;

/// <summary>
/// Canonical runtime-state scope and action tokens used by checkpointing and path helpers.
/// </summary>
public static class RuntimeStateScopes
{
    public const string RootAuthoritative = ".migration";
    public const string ProjectAuthoritative = "{org}/{project}/.migration";
    public const string RunAudit = ".migration/runs/{runId}";

    public const string InventoryAction = "inventory";
    public const string ExportAction = "export";
    public const string PrepareAction = "prepare";
    public const string ImportAction = "import";
    public const string ValidateAction = "validate";
    public const string DependenciesAction = "dependencies";
}
