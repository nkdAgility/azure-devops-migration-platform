using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
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
                catch (ArgumentException) { return SpectreValidationResult.Error("--output path is not valid"); }

                return SpectreValidationResult.Success();
            }
        }

        protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
        {
            var hostSettings = new MigrationPlatformHost.Settings(
                new Uri(settings.CollectionUrl),
                settings.Project,
                Path.GetFullPath(settings.OutputFolder));

            var host = MigrationPlatformHost.CreateDefaultBuilder(
                context.Arguments.ToArray(), hostSettings).Build();

            var agent = new TfsExportAgent(
                host.Services.GetRequiredService<IArtefactStore>(),
                host.Services.GetRequiredService<ICheckpointingService>(),
                host.Services.GetRequiredService<WorkItemStore>(),
                host.Services.GetRequiredService<IWorkItemRevisionMapper>(),
                host.Services.GetRequiredService<IAttachmentDownloader>(),
                host.Services.GetRequiredService<TfsWorkItemQueryWindowStrategy>(),
                host.Services.GetRequiredService<ILogger<TfsWorkItemRevisionSource>>(),
                host.Services.GetRequiredService<ILogger<TfsAttachmentBinarySource>>());
            var wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{settings.Project}'";

            // When stdout is redirected (subprocess mode) use NDJSON sink so the
            // parent process can parse progress events.  Otherwise render visually.
            if (Console.IsOutputRedirected)
            {
                var sink = new StdoutProgressSink();
                await agent.RunAsync(settings.Project, wiqlQuery, sink, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await AnsiConsole.Status()
                    .StartAsync("Exporting Work Items...", async ctx =>
                    {
                        ctx.Spinner(Spinner.Known.Dots);
                        ctx.SpinnerStyle(Style.Parse("green"));

                        await agent.RunAsync(
                            settings.Project, wiqlQuery,
                            new DelegateProgressSink(evt =>
                            {
                                ctx.Status(
                                    "Exporting Work Items\n" +
                                    "[bold yellow]Total:[/] " + evt.TotalWorkItems.ToString().PadRight(6) +
                                    "  [bold yellow]Done:[/] " + evt.WorkItemsProcessed.ToString().PadRight(6) + "\n" +
                                    "[bold yellow]Revisions:[/] " + evt.RevisionsProcessed.ToString().PadRight(6) +
                                    "  [bold yellow]Current WI:[/] " + evt.WorkItemId);
                            }),
                            cancellationToken).ConfigureAwait(false);
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

