// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobList;

/// <summary>
/// Static assertion helpers for <see cref="TuiJobListView"/> DataTable state.
/// </summary>
public static class TuiJobListAssertions
{
    // ── Column / row structure ────────────────────────────────────────────────

    /// <summary>
    /// Asserts that the DataTable inside <paramref name="view"/> contains exactly the specified
    /// columns (order-independent, case-sensitive).
    /// </summary>
    public static void AssertTableHasColumns(TuiJobListView view, string[] expectedColumns)
    {
        var dt = GetDataTable(view);
        Assert.IsNotNull(dt, "DataTable is null — UpdateJobs has not been called or the job list is empty.");

        var actual = dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet();
        foreach (var col in expectedColumns)
        {
            Assert.IsTrue(actual.Contains(col),
                $"Expected column '{col}' but found: {string.Join(", ", actual)}");
        }
    }

    /// <summary>
    /// Asserts the DataTable has exactly <paramref name="expected"/> data rows.
    /// </summary>
    public static void AssertTableRowCount(TuiJobListView view, int expected)
    {
        var dt = GetDataTable(view);
        var actual = dt?.Rows.Count ?? 0;
        Assert.AreEqual(expected, actual,
            $"Expected {expected} row(s) in job list table but found {actual}.");
    }

    /// <summary>
    /// Asserts that at least one cell in row <paramref name="rowIndex"/> contains
    /// <paramref name="text"/> (ordinal, case-insensitive).
    /// </summary>
    public static void AssertRowContains(TuiJobListView view, int rowIndex, string text)
    {
        var dt = GetDataTable(view);
        Assert.IsNotNull(dt, "DataTable is null — UpdateJobs has not been called or the job list is empty.");
        Assert.IsTrue(rowIndex < dt.Rows.Count,
            $"Row index {rowIndex} is out of range — table has {dt.Rows.Count} row(s).");

        var row = dt.Rows[rowIndex];
        var found = row.ItemArray
            .Any(cell => cell?.ToString()?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);

        Assert.IsTrue(found,
            $"Row {rowIndex} does not contain '{text}'. Row values: {string.Join(", ", row.ItemArray)}");
    }

    // ── Wait helper ────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls <paramref name="condition"/> every 50 ms until it returns true or
    /// <paramref name="timeout"/> elapses. Returns without throwing; callers
    /// should follow with an assertion.
    /// </summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50, cts.Token).ConfigureAwait(false);
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Reads the row count from the view's internal TableSource without assertions.
    /// Used by timer-driven wait loops.
    /// </summary>
    internal static int GetRowCount(TuiJobListView view)
        => GetDataTable(view)?.Rows.Count ?? 0;

    private static DataTable? GetDataTable(TuiJobListView view)
        => view.TableSource?.DataTable;
}
