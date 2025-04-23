using Microsoft.Extensions.DependencyInjection;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.TfsObjectModel;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace MigrationPlatform.CLI.Commands
{
    public class ExportCommand : AsyncCommand<ExportCommand.Settings>
    {
        public ExportCommand()
        {

        }

        public class Settings : CommandSettings
        {
            [CommandOption("--tfsserver <tfsserver>")]
            [Description("Url to your TFS server (default: https://localhost:8080/tfs)")]
            public string TfsServer { get; set; } = "https://localhost:8080/tfs";

            [CommandOption("--project <project>")]
            [Description("Name of the config location to use (default: default)")]
            public string Project { get; set; } = "default";

            [CommandOption("--output <outputFolder>")]
            [Description("The output location (default: ./output/test)")]
            public string OutputFolder { get; set; } = "./output/test";


            public override ValidationResult Validate()
            {
                if (string.IsNullOrWhiteSpace(TfsServer))
                {
                    return ValidationResult.Error("TFS server URL must be provided.");
                }

                if (!Uri.TryCreate(TfsServer, UriKind.Absolute, out var uriResult) || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                {
                    return ValidationResult.Error("TFS server must be a valid HTTP or HTTPS URL.");
                }

                if (string.IsNullOrWhiteSpace(Project))
                {
                    return ValidationResult.Error("Project name must be provided.");
                }

                if (string.IsNullOrWhiteSpace(OutputFolder))
                {
                    return ValidationResult.Error("Output folder must be provided.");
                }

                try
                {
                    var fullPath = Path.GetFullPath(OutputFolder);
                }
                catch (Exception)
                {
                    return ValidationResult.Error("Output folder path is not valid.");
                }

                return ValidationResult.Success();
            }
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var table = new Table()
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .AddColumn("[bold]Setting[/]")
                .AddColumn("[bold]Value[/]");

            table.AddRow("TFS Server", settings.TfsServer);
            table.AddRow("Project", settings.Project);
            table.AddRow("Output Folder", settings.OutputFolder);

            AnsiConsole.Write(new Panel(table)
                .Header("[bold green]Export Configuration[/]")
                .Padding(1, 1)
                .BorderColor(Color.Green));

            var host = MigrationPlatformHost.CreateDefaultBuilder(context.Arguments.ToArray(), settings.OutputFolder).Build();
            var workItemExportService = host.Services.GetRequiredService<IWorkItemExportService>();

            await AnsiConsole.Status()
                .StartAsync("Exporting Work Items...", async ctx =>
                {
                    ctx.Spinner(Spinner.Known.Dots);
                    ctx.SpinnerStyle(Style.Parse("green"));

                    // Do the export
                    await foreach (var wiStat in workItemExportService.ExportWorkItemsAsync(settings.TfsServer, settings.Project))
                    {
                        ctx.Status($"""
                            [bold yellow]WorkItem[/]: {wiStat.WorkItemId,-6} 
                            [bold yellow]Revision[/]: {wiStat.RevisionIndex,-3} 
                            [bold green]TWI[/]: {wiStat.WorkItemsProcessed,-5} 
                            [bold green]TRV[/]: {wiStat.RevisionsProcessed,-5} 
                            [bold green]TFLD[/]: {wiStat.FieldsProcessed,-5} 
                            [grey]Chunk[/]: {wiStat.ChunkInfo?.WorkItemsInChunk ?? 0,-5} 
                            {wiStat.ChunkInfo?.ChunkStart:yyyy-MM-dd} → {wiStat.ChunkInfo?.ChunkEnd:yyyy-MM-dd}
                        """);

                    }

                    // Optionally update output
                    AnsiConsole.MarkupLine("[green]✓ Exported work items[/]");
                });

            AnsiConsole.MarkupLineInterpolated(
                $"✅ [green]Discovery Complete[/] File saved to discovery-summary.csv"
            );
            return 0;
        }
    }
}
