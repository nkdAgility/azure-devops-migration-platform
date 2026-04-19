#if !NET481
using System;

namespace DevOpsMigrationPlatform.Infrastructure.Serialization;

/// <summary>Holds a single endpoint options type registration entry.</summary>
internal sealed record EndpointOptionsRegistration(string Key, Type Type, bool IsOrganisationEntry = false);
#endif
