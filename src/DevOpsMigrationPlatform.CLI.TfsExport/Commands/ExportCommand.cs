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

namespace DevOpsMigrationPlatform.CLI.TfsExport.Commands
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
            var configTable = new Table()
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .AddColumn("[bold]Setting[/]")
                .AddColumn("[bold]Value[/]");

            configTable.AddRow("Collection", settings.CollectionUrl);
            configTable.AddRow("Project", settings.Project);
            configTable.AddRow("Output folder", Path.GetFullPath(settings.OutputFolder));

            AnsiConsole.Write(new Panel(configTable)
                .Header("[bold green]Export Configuration[/]")
                .Padding(1, 1)
                .BorderColor(Color.Green));

            var hostSettings = new MigrationPlatformHost.Settings(
                new Uri(settings.CollectionUrl),
                settings.Project,
                Path.GetFullPath(settings.OutputFolder));

            var host = MigrationPlatformHost.CreateDefaultBuilder(
                context.Arguments.ToArray(), hostSettings).Build();

            var exportService = host.Services.GetRequiredService<IWorkItemExportService>();
            var wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{settings.Project}'";

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            await AnsiConsole.Status()
                .StartAsync("Exporting Work Items...", async ctx =>
                {
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("green"));

                    await foreach (var progress in exportService.ExportWorkItemsAsync(
                        settings.CollectionUrl, settings.Project, wiqlQuery, cts.Token))
                    {
                        ctx.Status($"""
                            Exporting Work Items
                            [bold yellow]Total:[/] {progress.TotalWorkItems,-6}  [bold yellow]Done:[/] {progress.WorkItemsProcessed,-6}
                            [bold yellow]Revisions:[/] {progress.RevisionsProcessed,-6}  [bold yellow]Current WI:[/] {progress.WorkItemId,-6}
                            [grey]Chunk:[/] {progress.ChunkInfo?.WorkItemsInChunk ?? 0,-5}  {progress.ChunkInfo?.ChunkStart:yyyy-MM-dd} → {progress.ChunkInfo?.ChunkEnd:yyyy-MM-dd}
                        """);
                    }
                });

            AnsiConsole.MarkupLine("[green]✅ Export complete.[/]");
            AnsiConsole.MarkupLineInterpolated($"Package written to [blue]{Path.GetFullPath(settings.OutputFolder)}[/]");
            return 0;
        }
    }
}

