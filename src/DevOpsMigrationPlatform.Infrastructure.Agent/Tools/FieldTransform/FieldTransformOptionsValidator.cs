// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform;

/// <summary>
/// Validates <see cref="FieldTransformOptions"/> at host startup via <c>ValidateOnStart()</c>.
/// Rejects rules with missing <see cref="FieldTransformRuleOptions.Type"/> and invalid
/// <see cref="FieldTransformRuleOptions.Pattern"/> regex values before any migration work begins.
/// </summary>
internal sealed class FieldTransformOptionsValidator : IValidateOptions<FieldTransformOptions>
{
    public ValidateOptionsResult Validate(string? name, FieldTransformOptions options)
    {
        var errors = new List<string>();

        for (int g = 0; g < options.TransformGroups.Count; g++)
        {
            var group = options.TransformGroups[g];
            if (!group.Enabled) continue;

            for (int r = 0; r < group.Transforms.Count; r++)
            {
                var rule = group.Transforms[r];
                if (!rule.Enabled) continue;

                var location = group.Name is not null
                    ? $"TransformGroups[{g}] ('{group.Name}').Transforms[{r}]"
                    : $"TransformGroups[{g}].Transforms[{r}]";

                if (string.IsNullOrWhiteSpace(rule.Type))
                    errors.Add($"{location}.Type is required.");

                if (rule.Pattern is not null)
                {
                    try
                    {
                        _ = new Regex(rule.Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                    }
                    catch (ArgumentException ex)
                    {
                        errors.Add($"{location}.Pattern '{rule.Pattern}' is not a valid regular expression: {ex.Message}");
                    }
                }
            }
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
#endif
