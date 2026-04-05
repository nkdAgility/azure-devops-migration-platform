using System;

namespace DevOpsMigrationPlatform.Abstractions.Utilities;

/// <summary>
/// Resolves <c>accessToken</c> field values.
/// Compiled for both net481 and net10.0.
/// </summary>
public static class TokenResolver
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
    public static string? Resolve(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;

        if (raw.StartsWith(EnvPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var varName = raw.Substring(EnvPrefix.Length);
            var value = Environment.GetEnvironmentVariable(varName);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException(
                    $"Token resolution failed: environment variable '{varName}' is not set or is empty.");
            return value;
        }

        return raw;
    }
}
