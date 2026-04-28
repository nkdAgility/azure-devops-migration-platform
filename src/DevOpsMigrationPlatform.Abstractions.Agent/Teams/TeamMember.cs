#if !NET481
namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>A single team member with their admin flag.</summary>
public sealed record TeamMember(
    string Descriptor,
    string DisplayName,
    string UniqueName,
    bool IsAdmin);
#endif
