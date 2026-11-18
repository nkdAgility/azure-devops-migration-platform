// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// A job's configuration cannot be run, with a message written for the operator that says what
/// to change. The message carries no secrets or raw exception text, so the agent sends it on the
/// job's progress stream before failing the job (PR #222 review); other failures send a generic
/// message and leave the detail in the agent log.
/// </summary>
public sealed class JobConfigurationException : Exception
{
    public JobConfigurationException(string operatorMessage)
        : base(operatorMessage)
    {
    }
}
