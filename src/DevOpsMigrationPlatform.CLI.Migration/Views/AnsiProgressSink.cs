// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Forwards <see cref="ProgressEvent"/> instances as Spectre.Console markup to the
/// active <see cref="AnsiConsole"/>. Used as the <see cref="IProgressSink"/> when the
/// TFS export subprocess is launched from the CLI in standalone (non-Control Plane) mode.
/// </summary>
internal sealed class AnsiProgressSink : IProgressSink
{
    public void Emit(ProgressEvent evt) =>
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]{evt.Module}[/] [dim]{evt.Stage}[/] {evt.Message ?? string.Empty}");
}
