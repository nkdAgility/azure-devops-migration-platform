// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Views;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI.JobDetail;

/// <summary>
/// Test implementation of <see cref="IUiDispatcher"/> that invokes actions
/// synchronously on the calling thread — no Terminal.Gui application loop required.
/// </summary>
internal sealed class DirectUiDispatcher : IUiDispatcher
{
    public void Invoke(Action action) => action();
}
