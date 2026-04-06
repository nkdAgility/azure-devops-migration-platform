using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Outcome of configuration or connectivity validation
/// </summary>
public class ValidationResult
{
    /// <summary>
    /// Overall validation success
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Specific error descriptions
    /// </summary>
    public List<string> ErrorMessages { get; }

    /// <summary>
    /// Non-blocking issues
    /// </summary>
    public List<string> WarningMessages { get; }

    /// <summary>
    /// Validation timestamp
    /// </summary>
    public DateTime ValidatedAt { get; }

    /// <summary>
    /// What was being validated (Configuration, Connectivity, Permissions)
    /// </summary>
    public string Context { get; }

    private ValidationResult(bool isValid, string context, List<string> errors, List<string> warnings)
    {
        IsValid = isValid;
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ErrorMessages = errors ?? new List<string>();
        WarningMessages = warnings ?? new List<string>();
        ValidatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a successful validation result
    /// </summary>
    public static ValidationResult Success(string context, List<string>? warnings = null)
    {
        return new ValidationResult(true, context, new List<string>(), warnings ?? new List<string>());
    }

    /// <summary>
    /// Creates a failed validation result
    /// </summary>
    public static ValidationResult Failure(string context, List<string> errors, List<string>? warnings = null)
    {
        return new ValidationResult(false, context, errors, warnings ?? new List<string>());
    }

    /// <summary>
    /// Creates a failed validation result with a single error
    /// </summary>
    public static ValidationResult Failure(string context, string error)
    {
        return new ValidationResult(false, context, new List<string> { error }, new List<string>());
    }

    /// <summary>
    /// Gets a formatted error message for test output
    /// </summary>
    public string GetFormattedMessage()
    {
        if (IsValid)
        {
            var message = $"{Context} validation successful.";
            if (WarningMessages.Any())
            {
                message += $" Warnings: {string.Join("; ", WarningMessages)}";
            }
            return message;
        }
        
        return $"{Context} validation failed: {string.Join("; ", ErrorMessages)}";
    }
}