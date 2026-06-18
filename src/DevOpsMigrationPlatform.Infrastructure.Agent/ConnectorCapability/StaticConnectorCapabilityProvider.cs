// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Infrastructure.Agent.ConnectorCapability;

/// <summary>
/// Capability provider that stores a fixed <see cref="Abstractions.Agent.ConnectorCapability"/> flags
/// value declared at registration time. <see cref="Has"/> performs a simple bitwise test.
/// </summary>
public sealed class StaticConnectorCapabilityProvider
    : DevOpsMigrationPlatform.Abstractions.Agent.IConnectorCapabilityProvider
{
    private readonly DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability _declared;

    /// <summary>
    /// Initialises the provider with the declared capability flags for this connector.
    /// </summary>
    public StaticConnectorCapabilityProvider(
        DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability declared)
    {
        _declared = declared;
    }

    /// <inheritdoc />
    public bool Has(DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability capability)
        => capability == DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability.None
            || (_declared & capability) == capability;
}
