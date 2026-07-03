// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Caps = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Agent;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;

/// <summary>
/// Shared connector-capability declarations for tests (ADR-0024, EC-H1).
/// </summary>
internal static class TestConnectorCapabilities
{
    /// <summary>Declares every capability — the ADO/Simulated posture.</summary>
    public static readonly IConnectorCapabilityProvider All = new DevOpsMigrationPlatform.Infrastructure.Agent.ConnectorCapability.StaticConnectorCapabilityProvider(
        Caps.BoardConfig |
        Caps.TaskboardColumns |
        Caps.Backlogs |
        Caps.TeamCapabilities |
        Caps.WorkItemComments);

    /// <summary>Declares no capability — the TFS Object Model posture.</summary>
    public static readonly IConnectorCapabilityProvider None = new DevOpsMigrationPlatform.Infrastructure.Agent.ConnectorCapability.StaticConnectorCapabilityProvider(
        Caps.None);
}
