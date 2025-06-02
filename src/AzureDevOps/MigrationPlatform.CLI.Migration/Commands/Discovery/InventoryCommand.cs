using Microsoft.Extensions.DependencyInjection;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.TfsObjectModel;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Globalization;

namespace MigrationPlatform.CLI.Commands
{
    public class InventoryCommand : AsyncCommand<InventoryCommand.Settings>
    {
        public InventoryCommand()
        {

        }


        public class Settings : AdoSettings
        {

        }


        public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
        {
            var host = MigrationPlatformHost.CreateDefaultBuilder().Build();
            var catalogService = host.Services.GetRequiredService<ICatalogService>();

            var projects = await catalogService.GetProjectsAsync(settings.Organisation, settings.Token);

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
                         table.AddRow(project);
                         var summary = new ProjectDiscoverySummary { ProjectName = project };
                         summaries.Add(summary);

                         // Work Items
                         await foreach (var wiStat in catalogService.CountAllWorkItemsAsync(settings.Organisation, project, settings.Token))
                         {
                             summary.WorkItemsCount = wiStat.WorkItemsCount;
                             summary.RevisionsCount = wiStat.RevisionsCount;
                             summary.IsWorkItemComplete = wiStat.IsWorkItemComplete;
                             summary.LastUpdatedUtc = wiStat.LastUpdatedUtc;
                             ctx.UpdateTarget(RenderTable(summaries));
                         }

                         // Repos
                         await foreach (var repoStat in catalogService.CountRepositoriesAsync(settings.Organisation, project, settings.Token))
                         {
                             summary.ReposCount = repoStat.ReposCount;
                             summary.IsRepoComplete = repoStat.IsRepoComplete;
                             summary.LastUpdatedUtc = repoStat.LastUpdatedUtc;
                             ctx.UpdateTarget(RenderTable(summaries));
                         }

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
