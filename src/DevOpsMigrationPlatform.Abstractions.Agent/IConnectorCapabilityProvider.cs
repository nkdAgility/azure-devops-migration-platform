// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent;

/// <summary>
/// Provides a runtime capability test for a connector.
/// Implementations are registered per connector — TFS registers an explicit
/// <see cref="ConnectorCapability.None"/> declaration so extension code never needs null-guards.
/// </summary>
public interface IConnectorCapabilityProvider
{
    /// <summary>
    /// Returns <see langword="true"/> if the connector supports the given <paramref name="capability"/>.
    /// For composite flags (e.g. <see cref="ConnectorCapability.BoardConfig"/>) returns
    /// <see langword="true"/> only when ALL constituent flags are set.
    /// </summary>
    bool Has(ConnectorCapability capability);
}
