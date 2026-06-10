// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Commands.Discovery;

/// <summary>
/// Settings for the <c>discovery inventory</c> command.
/// Accepts an explicit organisation URL and PAT directly (no config file required),
/// or falls back to a <c>--config</c> file for full multi-organisation scenarios.
/// </summary>
public sealed class InventoryCommandSettings : BaseCommandSettings
{
    /// <summary>
    /// Azure DevOps organisation URL (e.g. <c>https://dev.azure.com/myorg</c>).
    /// When supplied, --token must also be provided.
    /// </summary>
    [CommandOption("--organisation")]
    [Description("Azure DevOps organisation URL (e.g. https://dev.azure.com/myorg).")]
    public string? Organisation { get; init; }

    /// <summary>
    /// Personal Access Token used to authenticate with the Azure DevOps organisation.
    /// Required when --organisation is specified.
    /// </summary>
    [CommandOption("--token")]
    [Description("Personal Access Token for authenticating with the Azure DevOps organisation.")]
    public string? Token { get; init; }
}
