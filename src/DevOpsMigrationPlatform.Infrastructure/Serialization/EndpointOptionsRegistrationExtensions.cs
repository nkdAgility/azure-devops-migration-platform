// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Serialization;

/// <summary>
/// Extension methods that register connector-specific endpoint option types into the
/// <see cref="EndpointOptionsTypeRegistry"/> singleton so the polymorphic JSON converters
/// can deserialise scenario config files to the correct concrete type.
/// </summary>
public static class EndpointOptionsRegistrationExtensions
{
    /// <summary>
    /// Registers a <see cref="DevOpsMigrationPlatform.Abstractions.MigrationEndpointOptions"/>
    /// derived type for the given discriminator key.
    /// </summary>
    public static IServiceCollection AddEndpointOptionsType(
        this IServiceCollection services,
        string key,
        Type type)
    {
        services.AddSingleton(new EndpointOptionsRegistration(key, type, IsOrganisationEntry: false));
        return services;
    }

    /// <summary>
    /// Registers a <see cref="DevOpsMigrationPlatform.Abstractions.Options.OrganisationEntry"/>
    /// derived type for the given discriminator key.
    /// </summary>
    public static IServiceCollection AddOrganisationEntryType(
        this IServiceCollection services,
        string key,
        Type type)
    {
        services.AddSingleton(new EndpointOptionsRegistration(key, type, IsOrganisationEntry: true));
        return services;
    }
}
