namespace DevOpsMigrationPlatform.Infrastructure.Validation;

/// <summary>
/// Result of WIQL query validation.
/// </summary>
public sealed record WiqlValidationResult
{
    /// <summary>
    /// True if the WIQL query passed validation.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// User-friendly validation error message. Null if IsValid is true.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static WiqlValidationResult Success() => new() { IsValid = true, ErrorMessage = null };

    /// <summary>
    /// Creates a failed validation result with the given error message.
    /// </summary>
    public static WiqlValidationResult Failure(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}
