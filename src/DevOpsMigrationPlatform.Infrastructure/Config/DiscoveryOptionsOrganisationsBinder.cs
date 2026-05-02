// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Config;

/// <summary>
/// Post-configures <see cref="DiscoveryOptions.Organisations"/> by reading
/// the <c>MigrationPlatform:Organisations</c> config section and constructing
/// the correct concrete <see cref="OrganisationEntry"/> subtype for each element
/// based on the <c>Type</c> discriminator field.
/// <para>
/// <see cref="IConfiguration.Bind"/> cannot instantiate abstract types, so this
/// replaces the default binding for the polymorphic <c>Organisations</c> list.
/// </para>
/// </summary>
internal sealed class DiscoveryOptionsOrganisationsBinder : IPostConfigureOptions<DiscoveryOptions>
{
    private readonly IConfiguration _configuration;
    private readonly EndpointOptionsTypeRegistry _registry;

    public DiscoveryOptionsOrganisationsBinder(
        IConfiguration configuration,
        EndpointOptionsTypeRegistry registry)
    {
        _configuration = configuration;
        _registry = registry;
    }

    public void PostConfigure(string? name, DiscoveryOptions options)
    {
        if (options.Organisations.Count > 0)
            return; // already populated (e.g. by a test or manual setup)

        var section = _configuration.GetSection("MigrationPlatform:Organisations");
        if (!section.Exists())
            return;

        foreach (var child in section.GetChildren())
        {
            var type = child["Type"];
            if (string.IsNullOrWhiteSpace(type))
                throw new InvalidOperationException(
                    "Config error: An element in 'Organisations' is missing the 'Type' discriminator.");

            if (!_registry.TryGetOrganisationEntryType(type, out var concreteType) || concreteType is null)
                throw new InvalidOperationException(
                    $"Config error: Unknown organisation entry type '{type}'. " +
                    "Register the type with AddOrganisationEntryType() during DI setup.");

            var entry = (OrganisationEntry)(Activator.CreateInstance(concreteType)
                ?? throw new InvalidOperationException(
                    $"Failed to create instance of '{concreteType.FullName}'."));

            child.Bind(entry);
            options.Organisations.Add(entry);
        }
    }
}
#endif
