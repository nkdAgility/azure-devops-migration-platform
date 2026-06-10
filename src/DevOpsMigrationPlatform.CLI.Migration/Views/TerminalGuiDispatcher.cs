// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Production implementation of <see cref="IUiDispatcher"/> that marshals actions
/// onto the Terminal.Gui main loop via <see cref="Application.Invoke"/>.
/// </summary>
public sealed class TerminalGuiDispatcher : IUiDispatcher
{
    /// <inheritdoc/>
    public void Invoke(Action action) => Application.Invoke(action);
}
