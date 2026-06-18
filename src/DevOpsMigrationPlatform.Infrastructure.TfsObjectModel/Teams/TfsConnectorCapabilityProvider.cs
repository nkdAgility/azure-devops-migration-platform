// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;

/// <summary>
/// Explicit capability declaration for the TFS Object Model connector.
/// TFS has no board configuration API, so every capability check returns
/// <see langword="false"/>. Registered as <see cref="IConnectorCapabilityProvider"/>
/// so that <c>BoardConfigTeamExtension</c> never needs a null-guard on the provider.
/// </summary>
public sealed class TfsConnectorCapabilityProvider : IConnectorCapabilityProvider
{
    /// <inheritdoc />
    /// <remarks>
    /// Always returns <see langword="false"/> — TFS Object Model exposes
    /// <see cref="ConnectorCapability.None"/>.
    /// </remarks>
    public bool Has(ConnectorCapability capability) => false;
}
