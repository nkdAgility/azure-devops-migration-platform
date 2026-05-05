// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure;

/// <summary>
/// Validates <see cref="MigrationPlatformOptions"/> at host startup via <c>ValidateOnStart()</c>.
/// Failures abort the process before any migration work begins.
/// </summary>
internal sealed class MigrationPlatformOptionsValidator : IValidateOptions<MigrationPlatformOptions>
{
    private static readonly string[] ValidModes = ["Inventory", "Dependencies", "Export", "Prepare", "Import", "Migrate"];

    // Only AzureDevOpsServices may be specified as a migration source or target.
    // TeamFoundationServer is a valid source type (TFS on-premises) but never a target —
    // the platform always migrates INTO Azure DevOps Services.
    private static readonly string[] ValidSourceTypes = ["AzureDevOpsServices", "TeamFoundationServer", "Simulated"];
    private static readonly string[] ValidTargetTypes = ["AzureDevOpsServices", "Simulated"];

    public ValidateOptionsResult Validate(string? name, MigrationPlatformOptions options)
    {
        var errors = new List<string>();

        // Policies
        try { options.Policies.Validate(); }
        catch (InvalidOperationException ex) { errors.Add(ex.Message); }

        // Mode
        if (string.IsNullOrWhiteSpace(options.Mode))
            errors.Add("Mode is required (Inventory | Dependencies | Export | Prepare | Import | Migrate).");
        else if (!ValidModes.Contains(options.Mode, StringComparer.Ordinal))
            errors.Add($"Mode '{options.Mode}' is not valid. Must be one of: {string.Join(", ", ValidModes)}.");

        var requiresSource = options.Mode is "Export" or "Migrate";
        var requiresTarget = options.Mode is "Prepare" or "Import" or "Migrate";
        var requiresOrganisations = options.Mode is "Inventory" or "Dependencies";

        if (requiresSource && options.Source is null)
            errors.Add("Source is required when Mode is Export or Migrate.");

        // Source type validation
        if (options.Source is not null)
        {
            if (string.IsNullOrWhiteSpace(options.Source.Type))
                errors.Add("Source.Type is required.");
            else if (!ValidSourceTypes.Contains(options.Source.Type, StringComparer.Ordinal))
                errors.Add($"Source.Type '{options.Source.Type}' is not supported. " +
                           $"Valid values: {string.Join(", ", ValidSourceTypes)}.");

            options.Source.ValidateEndpointFields(errors, "Source");
        }

        if (requiresTarget && options.Target is null)
            errors.Add("Target is required when Mode is Prepare, Import, or Migrate.");

        if (requiresOrganisations && options.Organisations.Count == 0)
            errors.Add("Organisations is required when Mode is Inventory or Dependencies.");

        // Target type validation — TeamFoundationServer is never a valid target
        if (options.Target is not null)
        {
            if (string.IsNullOrWhiteSpace(options.Target.Type))
                errors.Add("Target.Type is required.");
            else if (!ValidTargetTypes.Contains(options.Target.Type, StringComparer.Ordinal))
                errors.Add($"Target.Type '{options.Target.Type}' is not supported. " +
                           $"Only '{string.Join(", ", ValidTargetTypes)}' is a valid migration target. " +
                           "TeamFoundationServer / Azure DevOps Server cannot be used as a migration target.");

            options.Target.ValidateEndpointFields(errors, "Target");
        }

        // Package path
        if (string.IsNullOrWhiteSpace(options.Package.WorkingDirectory))
            errors.Add("Package.WorkingDirectory is required.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
