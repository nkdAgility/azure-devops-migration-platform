// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default singleton holder for the current immutable <see cref="IAgentJobContext"/>.
/// </summary>
public sealed class CurrentAgentJobContextAccessor : ICurrentAgentJobContextAccessor
{
    private volatile IAgentJobContext? _current;

    public IAgentJobContext? Current => _current;

    public void Set(IAgentJobContext context)
    {
        _current = context ?? throw new ArgumentNullException(nameof(context));
    }

    public void Clear()
    {
        _current = null;
    }
}