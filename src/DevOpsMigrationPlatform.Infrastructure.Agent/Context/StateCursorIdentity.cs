// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Builds and parses action-qualified cursor identities.
/// </summary>
public static class StateCursorIdentity
{
    public static string Build(string action, string module)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(module))
            throw new ArgumentException("Module is required.", nameof(module));

        return $"{action.Trim().ToLowerInvariant()}.{module.Trim().ToLowerInvariant()}";
    }

    public static bool TryParse(string value, out string action, out string module)
    {
        action = string.Empty;
        module = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var index = value.IndexOf('.');
        if (index <= 0 || index >= value.Length - 1)
            return false;

        action = value.Substring(0, index).Trim().ToLowerInvariant();
        module = value.Substring(index + 1).Trim().ToLowerInvariant();
        return action.Length > 0 && module.Length > 0;
    }
}
