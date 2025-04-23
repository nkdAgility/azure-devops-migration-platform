using MigrationPlatform.Abstractions.Utilities;
using MigrationPlatform.CLI.Commands;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;

namespace MigrationPlatform.TfsExport
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
#if DEBUG
            if (!Debugger.IsAttached)
            {
                Debugger.Launch(); // Prompts to attach your debugger
            }
#endif

            var app = new CommandApp<ExportCommand>();
            app.Configure(config =>
            {
                config.SetApplicationName("tfsexport");
                config.SetApplicationVersion(VersionUtilities.GetRunningVersion().versionString);
#if DEBUG
                config.PropagateExceptions();
                config.ValidateExamples();
#endif
                config.AddCommand<ExportCommand>("export")
                    .WithDescription("Exports the data from TFS")
                .WithExample(new[] { "export", "--tfsserver", "https://localhost/tfs", "--project", "My Project" });
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
