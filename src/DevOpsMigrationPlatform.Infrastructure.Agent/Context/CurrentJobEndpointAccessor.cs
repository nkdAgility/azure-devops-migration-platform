// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default singleton holder for the current explicit source/target endpoint views.
/// </summary>
public sealed class CurrentJobEndpointAccessor : ICurrentJobEndpointAccessor
{
    private volatile ISourceEndpointInfo? _source;
    private volatile ITargetEndpointInfo? _target;

    public ISourceEndpointInfo? Source => _source;

    public ITargetEndpointInfo? Target => _target;

    public void SetSource(ISourceEndpointInfo endpoint)
    {
        _source = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    public void SetTarget(ITargetEndpointInfo endpoint)
    {
        _target = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
    }

    public void ClearSource()
    {
        _source = null;
    }

    public void ClearTarget()
    {
        _target = null;
    }

    public void Clear()
    {
        _source = null;
        _target = null;
    }
}