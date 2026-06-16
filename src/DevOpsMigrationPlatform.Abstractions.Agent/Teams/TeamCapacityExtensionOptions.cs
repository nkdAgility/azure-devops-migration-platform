// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Options for the TeamCapacity team extension. Each extension owns its own
/// <c>IOptions&lt;T&gt;</c> — no shared module-wide options god-object.
/// </summary>
public sealed class TeamCapacityExtensionOptions
#if NET7_0_OR_GREATER
    : DevOpsMigrationPlatform.Abstractions.Options.IConfigSection
#endif
{
    /// <summary>The canonical config section path for this options type.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams:Extensions:TeamCapacity";

    /// <summary>Whether the TeamCapacity extension is enabled. Default: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;
}
