// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Prevents run-scope audit paths from being used as authoritative state.
/// </summary>
public static class RunScopeAuthorityGuard
{
    public static bool IsRunScopedPath(string path)
        => !string.IsNullOrWhiteSpace(path) &&
           path.Replace('\\', '/').IndexOf(".migration/runs/", StringComparison.OrdinalIgnoreCase) >= 0;

    public static void EnsureAuthoritativePath(string path, string operation)
    {
        if (IsRunScopedPath(path))
            throw new InvalidOperationException(
                $"Run-scope path '{path}' cannot be used as authoritative state for '{operation}'.");
    }
}
