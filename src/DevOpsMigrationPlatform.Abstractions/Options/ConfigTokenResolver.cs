// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Resolves <c>accessToken</c> field values.
/// Compiled for both net481 and net10.0.
/// </summary>
public static class ConfigTokenResolver
{
    private const string EnvPrefix = "$ENV:";

    /// <summary>
    /// Resolves a raw token value:
    /// <list type="bullet">
    ///   <item>Returns <c>null</c> if <paramref name="raw"/> is null or empty.</item>
    ///   <item>If the value starts with <c>$ENV:VARNAME</c>, reads env var <c>VARNAME</c>.
    ///         Throws <see cref="InvalidOperationException"/> if the variable is unset or empty.</item>
    ///   <item>Otherwise returns the literal value unchanged.</item>
    /// </list>
    /// </summary>
    /// <param name="raw">The raw token value to resolve.</param>
    /// <param name="environmentReader">
    /// Optional environment-variable reader seam. Defaults to
    /// <see cref="Environment.GetEnvironmentVariable(string)"/> when <c>null</c>.
    /// </param>
    public static string? Resolve(string? raw, Func<string, string?>? environmentReader = null)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        if (raw!.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var varName = raw.Substring(EnvPrefix.Length);
            var value = (environmentReader ?? Environment.GetEnvironmentVariable)(varName);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(
                    $"Token resolution failed: environment variable '{varName}' is not set or is empty.");
            return value;
        }

        return raw;
    }
}
