using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI;

internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("devopsmigration");
#if DEBUG
            config.PropagateExceptions();
            config.ValidateExamples();
#endif
            config.AddBranch("discovery", branch =>
            {
                branch.SetDescription("Tools for finding out what we have and the implications of any migration");
                branch.AddCommand<InventoryCommand>("inventory")
                    .WithDescription("Discover the contents of your Azure DevOps organisation")
                    .WithExample("discovery", "inventory", "--organisation", "https://dev.azure.com/myorg", "--token", "<pat>");
            });

            config.AddCommand<TfsExportCommand>("tfsexport")
                .WithDescription("Export work items from an on-premises TFS / Azure DevOps Server collection")
                .WithExample("tfsexport",
                    "--collection", "http://tfs:8080/tfs/DefaultCollection",
                    "--project", "MyProject",
                    "--output", "./package");
        });

        try
        {
            AnsiConsole.Write(new FigletText("DevOps Migration").LeftJustified().Color(Color.Blue));
            AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
            return await app.RunAsync(args);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine("[red]❌ Unhandled exception during CLI execution[/]");
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
            return 1;
        }
    }
}
