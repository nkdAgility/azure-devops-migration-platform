// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates identity inventory (capture), descriptor export, import (lookup/resolution), and validation.
/// </summary>
public interface IIdentitiesOrchestrator
{
    /// <summary>
    /// Inventory phase: enumerates identities from <paramref name="identitySource"/> (when
    /// available) and merges the count into the project inventory file. Owns the enumeration
    /// loop, progress events, and metrics — the module is a thin façade.
    /// </summary>
    Task<TaskExecutionResult> CaptureAsync(
        IIdentitySource? identitySource,
        InventoryContext context,
        string fallbackOrgUrl,
        CancellationToken ct);

    Task ExportAsync(
        IIdentitySource identitySource,
        ExportContext context,
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

    /// <summary>
    /// Prepare phase: resolves source identities against the live target tenant via
    /// <see cref="Identity.IIdentityAdapter"/>, applying the ordered
    /// <see cref="Identity.IIdentityMatchingStrategy"/> list (UPN → display name). Matches are
    /// cached for the import/translate phases and a <c>prepare-report.json</c> is written.
    /// Implements steps 2–3 of the resolution order (GAP-001); explicit overrides (step 1)
    /// and the configured default (step 4) remain owned by the translation tool.
    /// </summary>
    Task PrepareAsync(
        PrepareContext context,
        string organisation,
        string project,
        CancellationToken ct);

    /// <summary>
    /// Returns the cached Prepare-phase target descriptor for <paramref name="sourceIdentity"/>,
    /// or <c>null</c> if it was not resolved by UPN/display-name matching. Synchronous, read-only.
    /// </summary>
    string? ResolvePrepared(string sourceIdentity);

    // Runtime-agnostic per FR-020: no interface-level #if guard. The net481 (TFS agent)
    // runtime models its reduced import capability explicitly at the call site
    // (IdentitiesModule returns Skipped) — not by hiding the method from the interface.
    Task ImportAsync(
        IIdentityTranslationTool? identityTranslationTool,
        ImportContext context,
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

    Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct);
}
