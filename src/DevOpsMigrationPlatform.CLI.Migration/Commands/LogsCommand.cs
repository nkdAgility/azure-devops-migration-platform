// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace DevOpsMigrationPlatform.CLI.Commands;

public sealed class LogsCommand : ControlPlaneCommandBase<LogsCommand.Settings>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve logs for")]
        public Guid JobId { get; init; }

        [CommandOption("--follow")]
        [Description("Follow live events via SSE until the job completes")]
        public bool Follow { get; init; }
    }

    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings, CancellationToken cancellationToken = default)
    {
        // Create command-specific host with control plane client
        await CreateHost(Environment.GetCommandLineArgs(), (services, config) =>
        {
            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });

            services.AddTransient<ILogsClient>(sp => sp.GetRequiredService<ControlPlaneClient>());
        });

        var console = GetRequiredService<IAnsiConsole>();

        try
        {
            return await RunCoreAsync(settings, console);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (HttpRequestException ex)
        {
            console.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
            return 1;
        }
    }

    private async Task<int> RunCoreAsync(Settings settings, IAnsiConsole console)
    {
        var client = GetRequiredService<ILogsClient>();
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler ctrlCHandler = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += ctrlCHandler;

        try
        {
            if (!settings.Follow)
            {
                var events = await client.GetProgressAsync(settings.JobId, cts.Token);
                foreach (var evt in events)
                    console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));
                return 0;
            }

            await foreach (var evt in client.FollowLogsAsync(settings.JobId, cts.Token))
                console.WriteLine(JsonSerializer.Serialize(evt, _jsonOptions));

            return 0;
        }
        finally
        {
            Console.CancelKeyPress -= ctrlCHandler;
        }
    }
}
