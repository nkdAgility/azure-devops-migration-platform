using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Options;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>
/// Displays a snapshot of <c>ProgressEvent</c> records for a job.
/// No <c>--follow</c> — live streaming is TUI-only.
/// See docs/cli.md for full specification.
/// </summary>
public sealed class ManageProgressCommand : ControlPlaneCommandBase<ManageProgressCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve progress events for.")]
        public string? JobId { get; init; }
    }

    protected override async Task<int> ExecuteInternalAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.JobId) || !Guid.TryParse(settings.JobId, out var jobId))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --job must be a valid GUID.");
            return 1;
        }

        await CreateHost(Environment.GetCommandLineArgs(), (services, _) =>
        {
            services.AddHttpClient<ControlPlaneClient>((sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<EnvironmentOptions>>().Value;
                client.BaseAddress = new Uri(opts.ControlPlane.BaseUrl);
            });
        });

        var client = GetRequiredService<ControlPlaneClient>();

        try
        {
            var events = await client.GetProgressAsync(jobId, cancellationToken);
            if (events.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No progress events found for this job.[/]");
                return 0;
            }

            foreach (var evt in events)
            {
                var msg = !string.IsNullOrEmpty(evt.Message) ? evt.Message : string.Empty;
                if (!string.IsNullOrEmpty(msg))
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]");
                else
                    AnsiConsole.MarkupLine($"[blue]{Markup.Escape(evt.Module)}[/] [grey]{Markup.Escape(evt.Stage)}[/]");
            }

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
