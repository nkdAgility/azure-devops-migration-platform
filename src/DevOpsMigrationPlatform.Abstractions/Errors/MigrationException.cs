using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Errors;

/// <summary>
/// Custom exception for migration operations that includes error categorization, exit codes, and guidance.
/// Enables proper error handling, logging, and recovery strategies based on error type.
/// </summary>
public sealed class MigrationException : Exception
{
    /// <summary>
    /// The category of this error, used to determine retry behavior and exit code.
    /// </summary>
    public MigrationErrorCategory Category { get; }

    /// <summary>
    /// The exit code to use when the application terminates due to this error.
    /// Enables callers to distinguish error types via exit codes.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Whether this error is retryable (e.g., transient network issues).
    /// Callers should implement exponential backoff + circuit breaker for retryable errors.
    /// </summary>
    public bool IsRetryable { get; }

    /// <summary>
    /// User-facing guidance on how to resolve this error.
    /// </summary>
    public string? Guidance { get; }

    /// <summary>
    /// Creates a new migration exception with a category, exit code, and guidance.
    /// </summary>
    public MigrationException(
        string message,
        MigrationErrorCategory category = MigrationErrorCategory.Unknown,
        int? exitCode = null,
        bool isRetryable = false,
        string? guidance = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
        ExitCode = exitCode ?? GetDefaultExitCode(category);
        IsRetryable = isRetryable;
        Guidance = guidance;
    }

    /// <summary>
    /// Gets the default exit code for a given error category.
    /// </summary>
    private static int GetDefaultExitCode(MigrationErrorCategory category) =>
        category switch
        {
            MigrationErrorCategory.Unknown => 1,
            MigrationErrorCategory.Authentication => 2,
            MigrationErrorCategory.RateLimited => 3,
            MigrationErrorCategory.ValidationError => 4,
            MigrationErrorCategory.Transient => 5,
            MigrationErrorCategory.ResourceCapacity => 6,
            MigrationErrorCategory.RemoteServerError => 7,
            MigrationErrorCategory.DataIntegrity => 8,
            MigrationErrorCategory.NotSupported => 9,
            MigrationErrorCategory.Canceled => 128,
            _ => 1
        };

    /// <summary>
    /// Creates a friendly error message including category, exit code, and guidance.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>
        {
            base.ToString(),
            $"Category: {Category}",
            $"Exit Code: {ExitCode}",
            $"Retryable: {IsRetryable}"
        };

        if (!string.IsNullOrEmpty(Guidance))
            parts.Add($"Guidance: {Guidance}");

        return string.Join(Environment.NewLine, parts);
    }
}
