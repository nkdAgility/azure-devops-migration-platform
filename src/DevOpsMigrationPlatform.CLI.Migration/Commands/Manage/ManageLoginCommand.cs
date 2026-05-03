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

/// <summary>Authenticates with a control plane endpoint and stores the session token.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class ManageLoginCommand : CommandBase<ManageLoginCommand.Settings>
{
    public sealed class Settings : BaseCommandSettings
    {
        [CommandOption("--url <URL>")]
        [Description("Control plane endpoint URL")]
        public string? Url { get; init; }
    }

    protected override Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        AnsiConsole.MarkupLine("[grey]manage login — not yet implemented.[/]");
        return Task.FromResult(1);
    }
}
