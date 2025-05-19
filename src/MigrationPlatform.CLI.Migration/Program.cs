using MigrationPlatform.Abstractions.Utilities;
using MigrationPlatform.CLI.Commands;
using MigrationPlatform.CLI.ConfigCommands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace MigrationPlatform.CLI
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {


            var app = new CommandApp();
            app.Configure(config =>
            {
                config.SetApplicationName("devopsmigration");
                config.SetApplicationVersion(VersionUtilities.GetRunningVersion().versionString);
#if DEBUG
                config.PropagateExceptions();
                config.ValidateExamples();
#endif
                config.AddBranch("config", branch =>
                {
                    branch.SetDescription("Tools manipulating and setting up configurations");
                    branch.AddCommand<ConfigSetConfigStorageCommand>("setfolder")
                        .WithDescription("Sets the folder to use to store your configurations")
                        .WithExample(new[] { "config", "setfolder", "--path", "%userprofile%\\AzureDevOpsMigrationTools" });
                    branch.AddCommand<ConfigSetConfigStorageCommand>("create")
                        .WithDescription("Add or update an Azure DevOps configuration. For example, which server or account plus auth information.")
                        .WithExample(new[] { "config", "create" });

                });

                config.AddBranch("discovery", branch =>
                {
                    branch.SetDescription("Tools for finding out what we have and the implications of any migration");
                    branch.AddCommand<DiscoveryCommand>("inventory")
                     .WithDescription("Discover the contents of your Azure DevOps instance")
                     .WithExample(new[] { "discovery", "inventory", "--organisation", "", "--token", "" });

                });



                config.AddCommand<TfsExportCommand>("tfsexport")
                   .WithDescription("Exports the data from TFS")
                   .WithExample(new[] { "tfsexport", "--tfsserver", "https://localhost/tfs", "--project", "My Project" });
            });

            try
            {
                AnsiConsole.Write(new FigletText("TFS Exporter").LeftJustified().Color(Color.Red));
                AnsiConsole.Write(new Rule().RuleStyle("grey").LeftJustified());
                await app.RunAsync(args);
                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[red]❌ Unhandled exception during CLI execution[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything | ExceptionFormats.ShowLinks);
                return 1;
            }

        }



    }
}
