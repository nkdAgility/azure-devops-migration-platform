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
/// Streams live diagnostic logs for an active job, or shows a message for completed jobs.
/// See docs/cli.md for full specification.
/// </summary>
public sealed class ManageDiagnosticsCommand : ControlPlaneCommandBase<ManageDiagnosticsCommand.Settings>
{
    public sealed class Settings : ControlPlaneBaseCommandSettings
    {
        [CommandOption("--job <JOB_ID>")]
        [Description("The job ID to retrieve diagnostic logs for.")]
        public string? JobId { get; init; }

        [CommandOption("--level")]
        [Description("Client-side level filter. Only show records at this level or above. Default: all.")]
        public string? Level { get; init; }
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
            var hasRecords = false;
            await foreach (var record in client.StreamDiagnosticsAsync(jobId, settings.Level, cancellationToken))
            {
                hasRecords = true;
                var levelColor = record.Level switch
                {
                    "Error" or "Critical" => "red",
                    "Warning" => "yellow",
                    "Debug" or "Trace" => "grey",
                    _ => "blue"
                };
                AnsiConsole.MarkupLine(
                    $"[grey]{record.Timestamp:HH:mm:ss.fff}[/] [{levelColor}]{Markup.Escape(record.Level)}[/] [[{Markup.Escape(record.Category)}]] {Markup.Escape(record.Message)}");

                if (record.Exception is not null)
                    AnsiConsole.MarkupLine($"  [grey]{Markup.Escape(record.Exception)}[/]");
            }

            if (!hasRecords)
                AnsiConsole.MarkupLine("[yellow]No diagnostic records found for this job. Historical logs are available in the job package.[/]");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
