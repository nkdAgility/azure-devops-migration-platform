// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;

namespace DevOpsMigrationPlatform.Infrastructure.Serialization;

/// <summary>
/// Thread-safe registry that maps endpoint type discriminator strings (e.g. <c>"AzureDevOpsServices"</c>)
/// to their concrete <see cref="DevOpsMigrationPlatform.Abstractions.MigrationEndpointOptions"/> subtype.
/// Registered as a singleton; connector assemblies call <see cref="Register"/> during DI setup.
/// </summary>
public sealed class EndpointOptionsTypeRegistry
{
    private readonly ConcurrentDictionary<string, Type> _endpointTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Type> _organisationEntryTypes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a concrete <see cref="DevOpsMigrationPlatform.Abstractions.MigrationEndpointOptions"/>
    /// subtype for the given discriminator key.
    /// Idempotent — registering the same key+type combination more than once is allowed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="key"/> is already registered with a DIFFERENT type.</exception>
    public void Register(string key, Type type)
    {
        if (_endpointTypes.TryGetValue(key, out var existing))
        {
            if (existing != type)
                throw new InvalidOperationException(
                    $"Endpoint options type key '{key}' is already registered as '{existing.FullName}'. Cannot register '{type.FullName}' for the same key.");
            return; // same type — idempotent
        }
        _endpointTypes[key] = type;
    }

    /// <summary>
    /// Attempts to look up the concrete endpoint options type for the given discriminator key.
    /// </summary>
    public bool TryGetType(string key, out Type? type) =>
        _endpointTypes.TryGetValue(key, out type);

    /// <summary>
    /// Registers a concrete <see cref="DevOpsMigrationPlatform.Abstractions.Options.OrganisationEntry"/>
    /// subtype for the given discriminator key.
    /// Idempotent — registering the same key+type combination more than once is allowed.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if <paramref name="key"/> is already registered with a DIFFERENT type.</exception>
    public void RegisterOrganisationEntry(string key, Type type)
    {
        if (_organisationEntryTypes.TryGetValue(key, out var existing))
        {
            if (existing != type)
                throw new InvalidOperationException(
                    $"Organisation entry type key '{key}' is already registered as '{existing.FullName}'. Cannot register '{type.FullName}' for the same key.");
            return; // same type — idempotent
        }
        _organisationEntryTypes[key] = type;
    }

    /// <summary>
    /// Attempts to look up the concrete organisation entry type for the given discriminator key.
    /// </summary>
    public bool TryGetOrganisationEntryType(string key, out Type? type) =>
        _organisationEntryTypes.TryGetValue(key, out type);
}
