#if NET7_0_OR_GREATER
namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Marker interface for options types that participate in schema registration.
/// Enforces compile-time presence of SectionName via C# 11 static abstract interface member.
/// </summary>
public interface IConfigSection
{
    /// <summary>
    /// The canonical config section path for this options type.
    /// Must match the path used in BindConfiguration().
    /// </summary>
    static abstract string SectionName { get; }
}
#endif
