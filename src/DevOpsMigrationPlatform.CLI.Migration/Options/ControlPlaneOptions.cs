// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel.DataAnnotations;

namespace DevOpsMigrationPlatform.CLI.Migration.Options;

/// <summary>
/// Configuration options for the control plane HTTP endpoint.
/// Bound from the <c>ControlPlane</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class ControlPlaneOptions
{
    public const string SectionName = "ControlPlane";

    /// <summary>Base URL of the running control plane, e.g. <c>http://localhost:5100</c>.</summary>
    [Required]
    public string BaseUrl { get; init; } = "http://localhost:5100";
}
