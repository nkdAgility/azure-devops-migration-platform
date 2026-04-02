using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpectreValidationResult = Spectre.Console.ValidationResult;

namespace DevOpsMigrationPlatform.CLI.TfsMigration.Commands
{
    public sealed class ExportCommand : AsyncCommand<ExportCommand.Settings>
    {
        public sealed class Settings : CommandSettings
        {
            [CommandOption("--collection <COLLECTION>")]
            [Description("URL of the TFS collection (e.g. http://tfs:8080/tfs/DefaultCollection)")]
            public string CollectionUrl { get; set; } = string.Empty;

            [CommandOption("--project <PROJECT>")]
            [Description("Team project name to export")]
            public string Project { get; set; } = string.Empty;

            [CommandOption("--output <OUTPUT>")]
            [Description("Root folder where the migration package will be written (default: ./package)")]
            public string OutputFolder { get; set; } = "./package";

            public override SpectreValidationResult Validate()
            {
                if (string.IsNullOrWhiteSpace(CollectionUrl))
                    return SpectreValidationResult.Error("--collection is required");

                if (!Uri.TryCreate(CollectionUrl, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    return SpectreValidationResult.Error("--collection must be a valid http or https URL");

                if (string.IsNullOrWhiteSpace(Project))
                    return SpectreValidationResult.Error("--project is required");

                try { Path.GetFullPath(OutputFolder); }
                catch { return SpectreValidationResult.Error("--output path is not valid"); }

                return SpectreValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var hostSettings = new MigrationPlatformHost.Settings(
                new Uri(settings.CollectionUrl),
                settings.Project,
                Path.GetFullPath(settings.OutputFolder));

            var host = MigrationPlatformHost.CreateDefaultBuilder(
                context.Arguments.ToArray(), hostSettings).Build();

            var exportService = host.Services.GetRequiredService<IWorkItemExportService>();
            var agent = new TfsExportAgent(exportService);
            var wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{settings.Project}'";

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            // When stdout is redirected (subprocess mode) use NDJSON sink so the
            // parent process can parse progress events.  Otherwise render visually.
            if (Console.IsOutputRedirected)
            {
                var sink = new StdoutProgressSink();
                await agent.RunAsync(settings.CollectionUrl, settings.Project, wiqlQuery, sink, cts.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                WorkItemMigrationProgress? last = null;

                // Display a spinner while agents emits events; capture the last known state.
                var sink = new DelegateProgressSink(evt =>
                {
                    // keep the last progress for the spinner label (captured by closure below)
                });

                // Re-run with a capturing delegate so the Status callback can read it.
                ProgressEvent? lastEvt = null;
                var captureSink = new DelegateProgressSink(evt => lastEvt = evt);

                await AnsiConsole.Status()
                    .StartAsync("Exporting Work Items...", async ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Dots);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        await agent.RunAsync(
                            settings.CollectionUrl, settings.Project, wiqlQuery,
                            new DelegateProgressSink(evt =>
                            {
                                lastEvt = evt;
                                ctx.Status(
                                    "Exporting Work Items\n" +
                                    "[bold yellow]Total:[/] " + evt.TotalWorkItems.ToString().PadRight(6) +
                                    "  [bold yellow]Done:[/] " + evt.WorkItemsProcessed.ToString().PadRight(6) + "\n" +
                                    "[bold yellow]Revisions:[/] " + evt.RevisionsProcessed.ToString().PadRight(6) +
                                    "  [bold yellow]Current WI:[/] " + evt.WorkItemId);
                            }),
                            cts.Token).ConfigureAwait(false);
                    });

                AnsiConsole.MarkupLine("[green]\u2705 Export complete.[/]");
                AnsiConsole.MarkupLineInterpolated($"Package written to [blue]{Path.GetFullPath(settings.OutputFolder)}[/]");
            }

            return 0;
        }
    }

    /// <summary>Simple IProgressSink backed by an Action — avoids a separate class file for one-off usage.</summary>
    internal sealed class DelegateProgressSink : IProgressSink
    {
        private readonly Action<ProgressEvent> _handler;
        public DelegateProgressSink(Action<ProgressEvent> handler) => _handler = handler;
        public void Emit(ProgressEvent evt) => _handler(evt);
    }
}

