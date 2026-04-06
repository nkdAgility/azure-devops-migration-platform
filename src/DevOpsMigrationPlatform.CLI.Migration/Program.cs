using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
        AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("devopsmigration");
#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#endif

            config.AddCommand<ConfigureCommand>("configure")
                .WithDescription("Interactive configuration wizard to create migration settings")
                .WithExample("configure")
                .WithExample("configure", "--output", "my-migration.json")
                .WithExample("configure", "--output", "my-migration.json", "--force");

            config.AddBranch("discovery", branch =>
            {
                branch.SetDescription("Tools for finding out what we have and the implications of any migration");

                branch.AddCommand<InventoryCommand>("inventory")
                    .WithDescription("Count work items and revisions per project")
                    .WithExample("discovery", "inventory", "--all-projects")
                    .WithExample("discovery", "inventory", "--output", "./inventory-results");
            });

            config.AddCommand<TfsExportCommand>("tfsexport")
                .WithDescription("Export work items from an on-premises TFS / Azure DevOps Server collection")
                .WithExample("tfsexport",
                    "--collection", "http://tfs:8080/tfs/DefaultCollection",
                    "--project", "MyProject",
                    "--output", "./package");

            config.AddCommand<LogsCommand>("logs")
                .WithDescription("Retrieve or tail live ProgressEvents for a running job")
                .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001")
                .WithExample("logs", "--job", "00000000-0000-0000-0000-000000000001", "--follow");
        });

        return await app.RunAsync(args);
    }
}
