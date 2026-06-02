// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Context provided to prepare-time import failure patterns.
/// </summary>
/// <param name="PrepareContext">Module prepare execution context.</param>
/// <param name="WorkItemsOptions">Resolved WorkItems module options for the current run.</param>
/// <param name="Organisation">Source organisation name used to scope package content reads.</param>
/// <param name="Project">Source project name used to scope package content reads.</param>
public sealed record ImportFailurePatternContext(
    PrepareContext PrepareContext,
    WorkItemsModuleOptions WorkItemsOptions,
    string Organisation,
    string Project);

