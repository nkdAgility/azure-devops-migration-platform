// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>Opens the interactive Terminal UI showing live job state.</summary>
[HideFromChannel(ReleaseChannel.Preview)]
public sealed class TuiCommand : ControlPlaneCommandBase<TuiCommandSettings>
{
    /// <summary>
    /// The TUI is a read-only observer — it never starts the in-process control plane.
    /// It always connects to an already-running instance.
    /// </summary>
    protected override bool StartsLocalStack => false;

    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        TuiCommandSettings settings,
        CancellationToken cancellationToken = default)
    {
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });

            services.AddTransient<IControlPlaneClient>(sp => sp.GetRequiredService<ControlPlaneClient>());
        });

        // Health check: ensure the control plane is reachable
        var client = GetRequiredService<ControlPlaneClient>();
        var console = AnsiConsole.Console;

        try
        {
            await client.GetAllJobsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var effectiveUrl = GetControlPlaneUrl();
            console.MarkupLine($"[red]\u2717 Cannot reach Control Plane at {Markup.Escape(effectiveUrl)}[/]");
            console.MarkupLine("[grey]Ensure the control plane is running and the Environment.ControlPlane.BaseUrl config is correct.[/]");
            return 1;
        }

        // Validate --job GUID before entering the TUI (T032)
        Guid? preSelectJobId = null;
        if (settings.Job is not null)
        {
            if (!Guid.TryParse(settings.Job, out var parsedJobId))
            {
                console.MarkupLine($"[red]\u2717 Invalid job ID: '{Markup.Escape(settings.Job)}' is not a valid GUID.[/]");
                return 1;
            }

            preSelectJobId = parsedJobId;
        }

        Application.Init();
        try
        {
            var mainView = new TuiMainView(client, GetControlPlaneUrl());

            if (preSelectJobId.HasValue)
                mainView.PreSelectJob(preSelectJobId.Value);

            Application.Run(mainView);
        }
        finally
        {
            Application.Shutdown();
        }

        return 0;
    }
}

