// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Testing.Dsl.SystemTests;

/// <summary>
/// Thin wrapper over Stopwatch that enforces a named time budget for system test scenarios.
/// </summary>
public sealed class SystemTestTimeBudget : IDisposable
{
    /// <summary>Standard CI system test budget: 5 minutes.</summary>
    public static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _budget;
    private readonly Stopwatch _stopwatch;

    private SystemTestTimeBudget(TimeSpan budget)
    {
        _budget = budget;
        _stopwatch = Stopwatch.StartNew();
    }

    /// <summary>Creates and starts a budget timer with the given limit.</summary>
    public static SystemTestTimeBudget StartFor(TimeSpan budget) => new(budget);

    /// <summary>Creates and starts the standard 5-minute CI budget.</summary>
    public static SystemTestTimeBudget StartFiveMinute() => new(FiveMinutes);

    /// <summary>Wall-clock time elapsed since the budget started.</summary>
    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>True when the budget has not been exceeded.</summary>
    public bool IsWithinBudget => _stopwatch.Elapsed <= _budget;

    /// <summary>
    /// Asserts that the budget has not expired.
    /// Fails the test if elapsed time exceeds the configured limit.
    /// </summary>
    public void AssertNotExpired()
    {
        Assert.IsTrue(
            IsWithinBudget,
            $"Execution exceeded the {_budget} budget. Elapsed: {_stopwatch.Elapsed}");
    }

    /// <inheritdoc/>
    public void Dispose() => _stopwatch.Stop();
}
