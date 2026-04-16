using System;

namespace DevOpsMigrationPlatform.CLI.Migration.Options;

/// <summary>
/// Resolves <c>$ENV:VAR_NAME</c> references in configuration values to their
/// corresponding environment variable values at runtime. Fails fast on missing
/// variables. Never logs resolved values.
/// </summary>
internal static class EnvironmentVariableResolver
{
    private const string Prefix = "$ENV:";

    /// <summary>
    /// Returns <c>true</c> if <paramref name="value"/> is an environment variable reference.
    /// </summary>
    public static bool IsEnvReference(string? value)
        => value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// If <paramref name="value"/> starts with <c>$ENV:</c>, reads the named variable.
    /// Otherwise returns <paramref name="value"/> as-is.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an <c>$ENV:</c> reference refers to an unset or empty variable.
    /// </exception>
    public static string Resolve(string? value, string fieldName)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;

        if (!value.StartsWith(Prefix, StringComparison.Ordinal))
            return value;

        var varName = value[Prefix.Length..];
        if (string.IsNullOrWhiteSpace(varName))
            throw new InvalidOperationException(
                $"Configuration field '{fieldName}' contains an empty $ENV: reference.");

        var resolved = Environment.GetEnvironmentVariable(varName);
        if (string.IsNullOrEmpty(resolved))
            throw new InvalidOperationException(
                $"Configuration field '{fieldName}' references environment variable '{varName}' which is not set or is empty.");

        return resolved;
    }
}
