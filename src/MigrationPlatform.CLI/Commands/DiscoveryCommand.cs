using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MigrationPlatform.CLI.Models;
using MigrationPlatform.CLI.Options;
using MigrationPlatform.CLI.Services;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Globalization;

namespace MigrationPlatform.CLI.Commands
{
    public class DiscoveryCommand : AsyncCommand<DiscoveryCommand.Settings>
    {
        private readonly MigrationPlatformOptions _platformOptions;
        private readonly ICatalogService _catalogService;

        public DiscoveryCommand(ICatalogService catalogService, IOptions<MigrationPlatformOptions> platformOptions)
        {
            _catalogService = catalogService;
            _platformOptions = platformOptions.Value;
        }


        public class Settings : GlobalSettings
        {

        }


        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {


            var projects = await _catalogService.GetProjectsAsync(settings.Organisation, settings.Token);

            var table = new Table().Centered();

            await AnsiConsole.Live(table)
                 .StartAsync(async ctx =>
                 {
                     var summaries = new List<ProjectDiscoverySummary>();

                     table.AddColumn("Project")
                         .AddColumn("Work Items")
                         .AddColumn("Revisions")
                         .AddColumn("Repos")
                         .AddColumn("Pipelines")
                         .AddColumn("Updated");
                     ctx.Refresh();

                     foreach (var project in projects)
                     {
                         table.AddRow(project.Name);
                         var summary = new ProjectDiscoverySummary { ProjectName = project.Name };
                         summaries.Add(summary);

                         // Work Items
                         await foreach (var wiStat in _catalogService.CountAllWorkItemsAsync(settings.Organisation, project.Name, settings.Token))
                         {
                             summary.WorkItemsCount = wiStat.WorkItemsCount;
                             summary.RevisionsCount = wiStat.RevisionsCount;
                             summary.IsWorkItemComplete = wiStat.IsWorkItemComplete;
                             summary.LastUpdatedUtc = wiStat.LastUpdatedUtc;
                             ctx.UpdateTarget(RenderTable(summaries));
                         }

                         //// Repos (pseudo-code)
                         //summary.TotalRepos = await _catalogService.CountReposAsync(project.Name);
                         //summary.IsRepoComplete = true;
                         //ctx.UpdateTarget(RenderTable(summaries));

                         //// Pipelines (pseudo-code)
                         //summary.TotalPipelines = await _catalogService.CountPipelinesAsync(project.Name);
                         //summary.IsPipelineComplete = true;
                         //ctx.UpdateTarget(RenderTable(summaries));
                     }

                     ctx.UpdateTarget(RenderTable(summaries));
                     SaveSummaryAsCsv(summaries, "discovery-summary.csv");
                 });


            AnsiConsole.MarkupLineInterpolated(
                $"✅ [green]Discovery Complete[/] File saved to discovery-summary.csv"
            );
            return 0;
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
