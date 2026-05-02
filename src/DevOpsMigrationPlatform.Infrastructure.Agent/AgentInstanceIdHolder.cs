// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Infrastructure.Agent;

/// <summary>
/// Holds the unique GUID that identifies this agent process instance.
/// Registered as a singleton in DI so both <c>JobAgentWorker</c> and
/// <c>PackageLockFileService</c> use the same instance ID.
/// </summary>
public sealed class AgentInstanceIdHolder
{
    public Guid AgentInstanceId { get; }

    public AgentInstanceIdHolder(Guid agentInstanceId)
    {
        AgentInstanceId = agentInstanceId;
    }
}

