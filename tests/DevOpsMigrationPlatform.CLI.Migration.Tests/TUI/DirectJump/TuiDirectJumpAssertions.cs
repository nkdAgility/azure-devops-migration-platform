// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.DirectJump;

/// <summary>
/// Assertion helpers specific to the TUI direct-jump (--job flag) capability.
/// </summary>
public static class TuiDirectJumpAssertions
{
    /// <summary>
    /// Asserts <paramref name="logView"/> has no lines (panel cleared state).
    /// </summary>
    public static void AssertLogPanelIsCleared(TuiLogView logView)
        => Assert.AreEqual(0, logView.Lines.Count,
            $"Expected Log Panel to be cleared (0 lines) but found {logView.Lines.Count} line(s).");
}
