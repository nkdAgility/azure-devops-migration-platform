// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>Re-queues a paused job for Migration Agent pickup.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageResumeCommand : ControlPlaneCommandBase<ManageResumeCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to resume")]
        public string? JobId { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage resume — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
