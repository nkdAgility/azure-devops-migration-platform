using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure;

/// <summary>
/// Validates <see cref="MigrationOptions"/> at host startup via <c>ValidateOnStart()</c>.
/// Failures abort the process before any migration work begins.
/// </summary>
internal sealed class MigrationOptionsValidator : IValidateOptions<MigrationOptions>
{
    private static readonly string[] ValidModes = ["Export", "Import", "Both"];

    // Only AzureDevOpsServices may be specified as a migration source or target.
    // TeamFoundationServer is a valid source type (TFS on-premises) but never a target —
    // the platform always migrates INTO Azure DevOps Services.
    private static readonly string[] ValidSourceTypes = ["AzureDevOpsServices", "TeamFoundationServer"];
    private static readonly string[] ValidTargetTypes = ["AzureDevOpsServices"];

    public ValidateOptionsResult Validate(string? name, MigrationOptions options)
    {
        var errors = new List<string>();

        // Policies
        try { options.Policies.Validate(); }
        catch (InvalidOperationException ex) { errors.Add(ex.Message); }

        // Mode
        if (string.IsNullOrWhiteSpace(options.Mode))
            errors.Add("Mode is required (Export | Import | Both).");
        else if (!ValidModes.Contains(options.Mode, StringComparer.Ordinal))
            errors.Add($"Mode '{options.Mode}' is not valid. Must be one of: {string.Join(", ", ValidModes)}.");

        // Source required for Export / Both
        if (!string.IsNullOrEmpty(options.Mode) &&
            (options.Mode == "Export" || options.Mode == "Both") &&
            options.Source is null)
            errors.Add("Source is required when Mode is Export or Both.");

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

        // Target required for Import / Both
        if (!string.IsNullOrEmpty(options.Mode) &&
            (options.Mode == "Import" || options.Mode == "Both") &&
            options.Target is null)
            errors.Add("Target is required when Mode is Import or Both.");

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

        // Artefacts path
        if (string.IsNullOrWhiteSpace(options.Artefacts.WorkingDirectory))
            errors.Add("Artefacts.WorkingDirectory is required.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
