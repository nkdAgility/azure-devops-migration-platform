using MigrationPlatform.Abstractions.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Globalization;

namespace MigrationPlatform.CLI.Commands
{
    public class TfsExportCommand : AsyncCommand<TfsExportCommand.Settings>
    {

        public TfsExportCommand()
        {
        }


        public class Settings : CommandSettings
        {
            [CommandOption("--tfsserver <tfsserver>")]
            [Description("Url to your TFS server (default: https://mytfs:8080/tfs)")]
            public string TfsServer { get; set; } = "https://localhost:8080/tfs";

            [CommandOption("--project <project>")]
            [Description("Name of the config location to use (default: default)")]
            public string Project { get; set; } = "default";
        }

        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            try
            {
                var exeRelativePath = @"..\..\..\..\MigrationPlatform.TfsExport\bin\Debug\net481\MigrationPlatform.TfsExport.exe";
                var exeFullPath = Path.GetFullPath(exeRelativePath);

                if (!File.Exists(exeFullPath))
                {
                    AnsiConsole.MarkupLineInterpolated($"❌ [red]Executable not found:[/] {exeFullPath}");
                    return -1;
                }

                var exitCode = await ExternalToolRunner.RunWithStreamingAsync(
                    exeFullPath,
                    "--migrate project.zip",
                    onOutput: line => Console.WriteLine($"[tool] {line}"),
                    onError: line => Console.Error.WriteLine($"[error] {line}")
                );

                if (exitCode != 0)
                {
                    AnsiConsole.MarkupLineInterpolated($"❌ [red]Tfs Export failed with exit code {exitCode}[/]");
                    return exitCode;
                }

                AnsiConsole.MarkupLineInterpolated(
                    $"✅ [green]Tfs Export Complete[/] Files saved to: [blue]{Path.GetDirectoryName(Path.GetFullPath("project.zip"))}[/]"
                );

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"❌ [red]Unexpected error:[/] {ex.Message}");
                return -99;
            }
        }


        private static Table RenderTable(List<ProjectDiscoverySummary> summaries)
        {
            var table = new Table()
                .Title("[bold yellow]Discovery Progress[/]")
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .AddColumn("Project")
                .AddColumn("Work Items")
                .AddColumn("Revisions")
                .AddColumn("Repos")
                .AddColumn("Pipelines")
                .AddColumn("Last Updated");

            foreach (var summary in summaries)
            {
                table.AddRow(
                    summary.ProjectName,
                    summary.WorkItemsCount.ToString(),
                    summary.RevisionsCount.ToString(),
                    summary.ReposCount.ToString(),
                    summary.PipelinesCount.ToString(),
                    $"[grey]{summary.LastUpdatedUtc:HH:mm:ss}[/]"
                );
            }

            return table;
        }

        public static void SaveSummaryAsCsv(IEnumerable<ProjectDiscoverySummary> summaries, string filePath)
        {
            using var writer = new StreamWriter(filePath);
            using var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture);

            csv.WriteHeader<ProjectDiscoverySummary>();
            csv.NextRecord();

            foreach (var summary in summaries)
            {
                csv.WriteRecord(summary);
                csv.NextRecord();
            }
        }


    }
}
