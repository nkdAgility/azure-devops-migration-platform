// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

/// <summary>
/// Shared test helpers for async enumerable mocking.
/// </summary>
internal static class TestAsyncHelpers
{
    /// <summary>
    /// Returns an empty <see cref="IAsyncEnumerable{T}"/> for use in Moq setups.
    /// </summary>
    public static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
