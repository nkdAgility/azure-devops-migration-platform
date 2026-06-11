// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Abstraction over UI-thread dispatch so that <see cref="TuiLogView"/> and other
/// Terminal.Gui views can be exercised in tests without an active Application loop.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Executes <paramref name="action"/> on the UI thread (or immediately in test contexts).</summary>
    void Invoke(Action action);
}
