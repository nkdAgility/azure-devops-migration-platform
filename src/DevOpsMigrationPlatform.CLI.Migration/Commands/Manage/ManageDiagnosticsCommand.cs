using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.JobRunners;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Commands.Manage;

/// <summary>
/// Downloads diagnostic logs from a completed job's package via the control plane
/// download endpoint. No <c>--follow</c> — live streaming is TUI-only.
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

        LogLevel? levelFilter = null;
        if (!string.IsNullOrEmpty(settings.Level) &&
            Enum.TryParse<LogLevel>(settings.Level, ignoreCase: true, out var parsed))
        {
            levelFilter = parsed;
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
            var records = await client.DownloadDiagnosticsAsync(jobId, cancellationToken);
            if (records is null || records.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No diagnostic records found for this job.[/]");
                return 0;
            }

            foreach (var record in records)
            {
                if (levelFilter is not null &&
                    Enum.TryParse<LogLevel>(record.Level, ignoreCase: true, out var rl) &&
                    rl < levelFilter.Value)
                {
                    continue;
                }

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

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 1;
        }
    }
}
