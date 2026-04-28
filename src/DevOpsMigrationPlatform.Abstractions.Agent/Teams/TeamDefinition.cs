#if !NET481
namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Core team metadata exported from the source system.</summary>
public sealed record TeamDefinition(
    string Id,
    string Name,
    string Description,
    bool IsDefault);
#endif
