// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default singleton holder for the current raw package configuration.
/// </summary>
public sealed class CurrentPackageConfigAccessor : ICurrentPackageConfigAccessor
{
    private volatile IConfiguration? _current;

    public IConfiguration? Current => _current;

    public void Set(IConfiguration configuration)
    {
        _current = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void Clear()
    {
        _current = null;
    }
}