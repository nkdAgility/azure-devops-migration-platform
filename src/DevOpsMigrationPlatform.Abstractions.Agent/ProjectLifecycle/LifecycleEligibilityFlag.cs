// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

/// <summary>
/// Declares whether a test run is eligible for ephemeral project lifecycle.
/// </summary>
public sealed class LifecycleEligibilityFlag
{
    public bool IsEnabled { get; init; }

    public ISet<string> Connectors { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string? NamePrefix { get; init; }

    public bool IsEligibleForConnector(string connectorType)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(connectorType))
            return false;

        return Connectors.Contains(connectorType);
    }

    public static LifecycleEligibilityFlag Disabled { get; } = new();

    public void Validate()
    {
        if (IsEnabled && !Connectors.Any())
            throw new InvalidOperationException("Lifecycle eligibility is enabled but no connectors were specified.");
    }
}
