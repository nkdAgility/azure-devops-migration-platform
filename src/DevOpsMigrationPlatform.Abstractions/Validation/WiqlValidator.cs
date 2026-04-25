using System;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Validation;

/// <summary>
/// Validates WIQL (Work Item Query Language) queries to prevent injection attacks
/// and ensure only safe, read-only operations are executed.
/// Implements fail-fast validation with clear error messages.
/// </summary>
public static class WiqlValidator
{
    /// <summary>
    /// Validates a WIQL query to ensure it is safe and read-only.
    /// Returns a validation result containing either success or a user-friendly error message.
    /// </summary>
    /// <param name="query">The WIQL query to validate. Can be null or empty (defaults to SELECT *).</param>
    /// <returns>WiqlValidationResult indicating whether the query is valid.</returns>
    public static WiqlValidationResult Validate(string? query)
    {
        // Empty or null queries are valid and will be replaced with SELECT * by downstream code
        if (string.IsNullOrWhiteSpace(query))
            return WiqlValidationResult.Success();

        var normalizedQuery = query.Trim();

        // Rule 1: Query must start with SELECT (case-insensitive)
        if (!normalizedQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return WiqlValidationResult.Failure(
                "WIQL query must start with SELECT. Queries like UPDATE, DELETE, DROP, or DECLARE are not allowed.");

        // Rule 2: Forbid destructive operations (UPDATE, DELETE, DROP, INSERT, CREATE, ALTER)
        if (ContainsDestructiveOperation(normalizedQuery))
            return WiqlValidationResult.Failure(
                "WIQL query cannot contain UPDATE, DELETE, DROP, INSERT, CREATE, or ALTER operations. Only SELECT queries are allowed.");

        // Rule 3: Forbid DECLARE statements (can be used for variable injection)
        if (ContainsDeclareStatement(normalizedQuery))
            return WiqlValidationResult.Failure(
                "WIQL query cannot contain DECLARE statements. Only SELECT queries are allowed.");

        return WiqlValidationResult.Success();
    }

    /// <summary>
    /// Detects if the query contains destructive operations (UPDATE, DELETE, DROP, INSERT, CREATE, ALTER).
    /// </summary>
    private static bool ContainsDestructiveOperation(string query)
    {
        // Match destructive keywords as whole words, case-insensitive
        return Regex.IsMatch(query, @"\b(UPDATE|DELETE|DROP|INSERT|CREATE|ALTER)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Detects DECLARE statements which can be used for variable injection.
    /// </summary>
    private static bool ContainsDeclareStatement(string query)
    {
        return Regex.IsMatch(query, @"\bDECLARE\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

