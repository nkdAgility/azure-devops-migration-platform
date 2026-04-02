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
    private static readonly string[] SupportedConfigVersions = ["1.0"];
    private static readonly string[] ValidModes = ["Export", "Import", "Both"];

    public ValidateOptionsResult Validate(string? name, MigrationOptions options)
    {
        var errors = new List<string>();

        // ConfigVersion
        if (!SupportedConfigVersions.Contains(options.ConfigVersion, StringComparer.Ordinal))
            errors.Add($"ConfigVersion '{options.ConfigVersion}' is not supported. " +
                       $"Supported: {string.Join(", ", SupportedConfigVersions)}.");

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

        // Target required for Import / Both
        if (!string.IsNullOrEmpty(options.Mode) &&
            (options.Mode == "Import" || options.Mode == "Both") &&
            options.Target is null)
            errors.Add("Target is required when Mode is Import or Both.");

        // Artefacts path
        if (string.IsNullOrWhiteSpace(options.Artefacts.Path))
            errors.Add("Artefacts.Path is required.");

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
