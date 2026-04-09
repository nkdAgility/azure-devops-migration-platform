using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Microsoft.Extensions.DependencyInjection;
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

        var resolvedUrl = MigrationPlatformHost.ResolveControlPlaneUrl(settings.Url);
        await CreateHost(Environment.GetCommandLineArgs(), resolvedUrl, (services, _) =>
        {
            services.AddHttpClient<ControlPlaneClient>((_, client) =>
            {
                client.BaseAddress = new Uri(resolvedUrl ?? "http://localhost:5100");
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
                if (evt.RevisionsProcessed > 0)
                    AnsiConsole.MarkupLine(
                        $"[blue]WorkItems[/]  [bold]{evt.WorkItemsProcessed}[/] work items / [bold]{evt.RevisionsProcessed}[/] revisions  [grey](wi#{evt.WorkItemId})[/]");
                else if (!string.IsNullOrEmpty(msg))
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(msg)}[/]");
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
