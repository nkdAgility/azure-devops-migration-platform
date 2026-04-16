using DevOpsMigrationPlatform.CLI.TfsMigration.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using System;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.CLI.TfsMigration
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            var app = new CommandApp();
            app.Configure(config =>
            {
                config.SetApplicationName("tfsmigration");
#if DEBUG
                config.ValidateExamples();
#endif
                config.AddCommand<ExportCommand>("export")
                    .WithDescription("Exports work items from an on-premises TFS / Azure DevOps Server collection")
                    .WithExample(new[] { "export", "--collection", "http://tfs:8080/tfs/DefaultCollection", "--project", "MyProject", "--output", "./package" });

                config.AddCommand<InventoryCommand>("inventory")
                    .WithDescription("Counts work items per project using date-chunked WIQL queries. Reads credentials from stdin JSON.")
                    .WithExample(new[] { "inventory", "--collection", "http://tfs:8080/tfs/DefaultCollection", "--all-projects" })
                    .WithExample(new[] { "inventory", "--collection", "http://tfs:8080/tfs/DefaultCollection", "--project", "MyProject" });
            });

            AnsiConsole.Write(new FigletText("TFS Migration").LeftJustified().Color(Color.Red));
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
            return await app.RunAsync(args);
        }
    }
}
