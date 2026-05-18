// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Validates <see cref="NodeTranslationOptions"/> at host startup via <c>ValidateOnStart()</c>.
/// Rejects mapping expressions that the runtime <see cref="NodeTranslationTool"/> cannot compile
/// for <see cref="NodeTranslationOptions.AreaPathMappings"/> and
/// <see cref="NodeTranslationOptions.IterationPathMappings"/> before any migration work begins.
/// </summary>
internal sealed class NodeTranslationOptionsValidator : IValidateOptions<NodeTranslationOptions>
{
#if NET7_0_OR_GREATER
    private const RegexOptions RuntimeRegexOptions = RegexOptions.IgnoreCase | RegexOptions.NonBacktracking | RegexOptions.Compiled;
#else
    private const RegexOptions RuntimeRegexOptions = RegexOptions.IgnoreCase | RegexOptions.Compiled;
#endif

    public ValidateOptionsResult Validate(string? name, NodeTranslationOptions options)
    {
        var errors = new List<string>();
        ValidateMappings(options.AreaPathMappings, nameof(options.AreaPathMappings), errors);
        ValidateMappings(options.IterationPathMappings, nameof(options.IterationPathMappings), errors);
        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static void ValidateMappings(
        IReadOnlyList<NodeMapping> mappings,
        string fieldName,
        List<string> errors)
    {
        for (int i = 0; i < mappings.Count; i++)
        {
            var mapping = mappings[i];
            if (string.IsNullOrWhiteSpace(mapping.Match))
            {
                errors.Add($"{fieldName}[{i}].Match is required and cannot be empty.");
                continue;
            }

            try
            {
                _ = new Regex(mapping.Match, RuntimeRegexOptions, TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                errors.Add($"{fieldName}[{i}].Match '{mapping.Match}' is not a valid regular expression: {ex.Message}");
            }
        }
    }
}
