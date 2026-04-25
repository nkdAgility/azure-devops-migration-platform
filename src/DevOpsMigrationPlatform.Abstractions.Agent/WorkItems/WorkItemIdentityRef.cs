namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Represents a User identity reference in a Work Item context (e.g., created by, modified by, assigned to).
/// </summary>
public record WorkItemIdentityRef
{
    /// <summary>
    /// Human-readable display name of the identity.
    /// </summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Unique identifier in the source system (e.g., email or account name).
    /// </summary>
    public string UniqueName { get; init; } = string.Empty;

    /// <summary>
    /// Source-specific descriptor for identity mapping (e.g., AAD object ID, email).
    /// </summary>
    public string Descriptor { get; init; } = string.Empty;
}
